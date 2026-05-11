# AgentHost.Indexing

Real code-indexing pipeline for the Expert Agents platform.

## Architecture

```
Repositories (git) ──► GitRepositoryFetcher
                              │
                        ChunkerSelector
                    ┌─────────┴─────────┐
              TreeSitterChunker   LineWindowChunker
                              │
                         IEmbedder
                    ┌────────┴─────────────┐
               MockEmbedder  VoyageEmbedder  AzureOpenAIEmbedder
                              │
              ┌───────────────┴────────────┐
         IVectorStore               IMetadataStore
         (QdrantVectorStore)         (PostgresMetadataStore)
              │                           │
              └─────────── HybridRetriever ───────────┘
```

### Key components

| Component | Purpose |
|---|---|
| `TreeSitterChunker` | Language-aware chunking (C#, TS/JS, Python, Java, Go) using declaration-boundary detection |
| `LineWindowChunker` | 200-line sliding-window fallback for unknown file types |
| `ChunkerSelector` | Routes to the right chunker by file extension |
| `MockEmbedder` | Deterministic hash-based pseudo-vectors for local dev/tests |
| `VoyageEmbedder` | HTTP calls to `voyage-code-3` (best-in-class for code) |
| `AzureOpenAIEmbedder` | Azure OpenAI `text-embedding-3-large` |
| `QdrantVectorStore` | Qdrant gRPC client (official `Qdrant.Client` package) |
| `PostgresMetadataStore` | Npgsql + Dapper; auto-migrates tables on startup |
| `IndexingWorker` | `BackgroundService` that polls `indexing_jobs` every 5 s |
| `HybridRetriever` | Vector top-100 + BM25-style keyword blending |
| `MockCodeIndexServiceCompat` | Drops into existing agent code; routes to real retriever when flag is on |

## Configuration

Add to `appsettings.json`:

```json
"Indexing": {
  "UseRealRetriever": false,
  "Qdrant": { "Host": "localhost", "GrpcPort": 6334, "ApiKey": null },
  "Postgres": { "ConnectionString": "Host=localhost;Database=experts;Username=experts;Password=experts" },
  "Embedder": { "Provider": "Mock", "Model": null, "ApiKey": null },
  "RepoCacheDir": "./repo-cache",
  "MaxFileSizeBytes": 1048576
}
```

### Embedder providers

| `Provider` | Notes |
|---|---|
| `Mock` (default) | No external calls; safe for local dev and CI |
| `Voyage` | Set `ApiKey` to your Voyage AI key. Model defaults to `voyage-code-3` |
| `AzureOpenAI` | Set `Endpoint` (your AOAI endpoint) and `ApiKey`. Model defaults to `text-embedding-3-large` |

### Enabling real retrieval

Once a repository is registered in the metadata store (insert a row into `repositories`)
and an indexing job has completed:

1. Set `Indexing:UseRealRetriever=true` via user-secrets or env var.
2. Choose a real embedder provider (Voyage or AzureOpenAI).
3. Restart the host.

The existing `FhirServerAgent` and `HealthcareComponentsAgent` will automatically use real retrieval
through `MockCodeIndexServiceCompat` — no agent code changes required.

## Docker compose

Start the full stack:

```bash
docker compose up -d
```

This brings up:
- `agenthost` — the .NET host
- `qdrant` — vector database (ports 6333 REST, 6334 gRPC)
- `postgres` — metadata store (port 5432)

## Seeding demo repositories

The two hardcoded demo repos are seeded on first startup if Postgres is available
and `UseRealRetriever=false` (no-op in that case).  To index them for real:

```sql
INSERT INTO repositories (id, name, url, default_branch)
VALUES
  ('fhir-server', 'Microsoft FHIR Server', 'https://github.com/microsoft/fhir-server', 'main'),
  ('healthcare-shared-components', 'Healthcare Shared Components',
   'https://github.com/microsoft/healthcare-shared-components', 'main');

INSERT INTO indexing_jobs (repo_id, kind, status)
VALUES
  ('fhir-server', 'full', 'pending'),
  ('healthcare-shared-components', 'full', 'pending');
```

Then set a real embedder and flip `UseRealRetriever=true`.

## Implementation note: TreeSitterChunker

`TreeSitterChunker` uses regex-based declaration boundary detection rather than
native tree-sitter bindings.  This avoids native-binary packaging complexity while
producing equivalent chunking quality for top-level declarations.  A future PR can
swap the internal implementation to use `tree-sitter` native bindings without
changing the caller surface.
