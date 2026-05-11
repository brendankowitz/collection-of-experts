# Dimension 4: Code RAG Pipeline for Repository Indexing

## Executive Summary

This research investigates how to build a production-grade code indexing pipeline that multi-agent systems can query for contextual code retrieval. The pipeline spans vector database selection, embedding models optimized for code, AST-based chunking via tree-sitter and Roslyn, incremental git-based indexing, and hybrid search (BM25 + vector). Key findings: **Qdrant** is the best vector DB for a .NET prototype due to its native .NET client (`Qdrant.Client` 1.7.0+), Aspire hosting integration, and `Microsoft.Extensions.VectorData` support [^413^][^415^]. **voyage-code-3** is the leading embedding model for code retrieval at $0.18/1M tokens with 200M free tokens, outperforming OpenAI text-embedding-3-large by 13.8% on 32 code retrieval datasets [^128^][^122^]. For AST-based chunking, the `TreeSitter.DotNet` NuGet package provides .NET bindings for tree-sitter with 28+ language grammars including C# [^384^], while **Roslyn** (`MSBuildWorkspace`) offers deep semantic analysis of C# solutions [^458^][^462^]. Azure AI Search provides built-in hybrid search (vector + BM25 with RRF merging) via the `Azure.Search.Documents` SDK [^359^][^360^].

**Recommendation for prototype**: Use Qdrant (Docker) + voyage-code-3 embeddings + TreeSitter.DotNet chunking + Roslyn for C#-specific metadata, exposed via ASP.NET Core Minimal APIs.

---

## Table of Contents

1. [Vector Database Selection for .NET](#1-vector-database-selection-for-net)
2. [Embedding Models for C# Code](#2-embedding-models-for-c-code)
3. [AST-Based Code Chunking](#3-ast-based-code-chunking)
4. [Incremental Git-Based Indexing](#4-incremental-git-based-indexing)
5. [Code Chunk Metadata Structure](#5-code-chunk-metadata-structure)
6. [Hybrid Search Implementation](#6-hybrid-search-implementation)
7. [Code Indexing Service Architecture](#7-code-indexing-service-architecture)
8. [Agent Query API Design](#8-agent-query-api-design)
9. [Token & Cost Estimates](#9-token--cost-estimates)
10. [Keeping the Index Fresh](#10-keeping-the-index-fresh)
11. [Sources & References](#sources--references)

---

## 1. Vector Database Selection for .NET

### 1.1 Comparison Matrix

| Database | .NET Client | Hybrid Search | Self-Host | Cloud | Best For |
|----------|-------------|---------------|-----------|-------|----------|
| **Qdrant** | `Qdrant.Client` 1.7.0+ | Custom (vector + payload filter) | Docker/K8s | Qdrant Cloud | Prototype, open-source, full control |
| **Azure AI Search** | `Azure.Search.Documents` | Built-in (RRF) | N/A | Fully managed | Enterprise, Microsoft ecosystem |
| **pgvector** | Npgsql + Dapper/EF Core | pgvector + full-text GIN | PostgreSQL | Azure Postgres | Existing PostgreSQL infrastructure |
| **Milvus** | `Milvus.Client` (community) | Built-in | Docker/K8s | Zilliz Cloud | Large scale, high throughput |

### 1.2 Qdrant (Recommended for Prototype)

Qdrant is a Rust-based open-source vector database with first-class .NET support through the official `Qdrant.Client` NuGet package [^415^].

**Installation:**
```bash
# Docker run
docker run -p 6333:6333 -p 6334:6334 -v $(pwd)/qdrant_storage:/qdrant/storage qdrant/qdrant

# .NET Client
dotnet add package Qdrant.Client --version 1.12.0
```

**C# Usage - Create Collection & Upsert:**
```csharp
using Qdrant.Client;
using Qdrant.Client.Grpc;

var client = new QdrantClient("localhost");

// Create collection with vector parameters
await client.CreateCollectionAsync("code_chunks",
    vectorsConfig: new VectorParamsMap
    {
        Map = {
            ["code_embedding"] = new VectorParams {
                Size = 1024,              // voyage-code-3 dimensions
                Distance = Distance.Cosine
            }
        }
    });

// Upsert a code chunk with payload metadata
var point = new PointStruct
{
    Id = (ulong)chunkId,
    Vectors = new Dictionary<string, float[]>
    {
        ["code_embedding"] = embeddingVector
    },
    Payload =
    {
        ["file_path"] = "/src/Services/PatientService.cs",
        ["line_start"] = 42,
        ["line_end"] = 89,
        ["symbol_type"] = "method",
        ["symbol_name"] = "GetPatientByIdAsync",
        ["parent_class"] = "PatientService",
        ["content"] = chunkText,
        ["language"] = "csharp",
        ["commit_hash"] = "abc123"
    }
};

await client.UpsertAsync("code_chunks", new[] { point });
```

**Search with Payload Filter:**
```csharp
var results = await client.SearchAsync("code_chunks",
    vector: queryEmbedding,
    vectorName: "code_embedding",
    limit: 10,
    filter: new Filter
    {
        Must = {
            new Condition { Field = new FieldCondition {
                Key = "language",
                Match = new Match { Keyword = "csharp" }
            }}
        }
    });
```

**Aspire Hosting Integration:**
```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);
var qdrant = builder.AddQdrant("qdrant")
    .WithLifetime(ContainerLifetime.Persistent);

var indexer = builder.AddProject<Projects.CodeIndexer>("indexer")
    .WithReference(qdrant);
```

**Semantic Kernel Integration:**
```csharp
using Microsoft.SemanticKernel.Connectors.Qdrant;

builder.Services.AddQdrantVectorStore(
    host: "localhost",
    port: 6333,
    https: false);
```

**Key Features:**
- Supports HNSW (Hierarchical Navigable Small World) index for fast approximate nearest neighbor [^414^]
- Payload indexing for filtered search
- Quantization (scalar, product) for memory reduction
- Multi-vector support per point
- gRPC and REST APIs
- `Microsoft.Extensions.VectorData` abstraction layer [^413^][^459^]

### 1.3 Azure AI Search (Enterprise Option)

Azure AI Search provides a fully managed experience with built-in hybrid search (BM25 + vector via RRF) [^359^].

```bash
dotnet add package Azure.Search.Documents --version 11.6.0
```

```csharp
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

// Define index schema
var index = new SearchIndex("code-index")
{
    Fields = new List<SearchField>
    {
        new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
        new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.StandardLucene },
        new SimpleField("file_path", SearchFieldDataType.String) { IsFilterable = true },
        new SimpleField("line_start", SearchFieldDataType.Int32) { IsFilterable = true },
        new SearchField("content_vector", SearchFieldDataType.Collection(SearchFieldDataType.Single))
        {
            IsSearchable = true,
            VectorSearchDimensions = 1024,
            VectorSearchProfileName = "vector-profile"
        }
    },
    VectorSearch = new VectorSearch
    {
        Profiles = { new VectorSearchProfile("vector-profile", "algorithm-config") },
        Algorithms = { new HnswAlgorithmConfiguration("algorithm-config") }
    },
    SemanticSearch = new SemanticSearchOptions
    {
        Configurations = {
            new SemanticConfiguration("semantic-config",
                new SemanticPrioritizedFields {
                    TitleField = new SemanticField("file_path"),
                    ContentFields = { new SemanticField("content") }
                })
        }
    }
};
```

**Hybrid Search (Vector + BM25):**
```csharp
// Hybrid search: text query + vector query merged via RRF
var searchOptions = new SearchOptions
{
    VectorSearch = new VectorSearchOptions
    {
        Queries = {
            new VectorizedQuery(queryEmbedding)
            {
                KNearestNeighborsCount = 10,
                Fields = { "content_vector" }
            }
        }
    },
    QueryType = SearchQueryType.Semantic,
    SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = "semantic-config" },
    Size = 10
};

var results = await searchClient.SearchAsync<CodeChunk>("patient service query", searchOptions);
```

### 1.4 pgvector (PostgreSQL Extension)

pgvector adds vector storage directly to PostgreSQL, ideal when vectors live alongside relational data [^357^].

```csharp
// Enable pgvector extension
await conn.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS vector");
conn.ReloadTypes();  // Refresh Npgsql type cache

// Create table with vector column
await conn.ExecuteAsync("""
    CREATE TABLE code_chunks (
        id SERIAL PRIMARY KEY,
        file_path TEXT NOT NULL,
        line_start INT,
        line_end INT,
        symbol_name TEXT,
        content TEXT,
        embedding vector(1024) NOT NULL
    )
""")

// Create HNSW index
await conn.ExecuteAsync("""
    CREATE INDEX code_embedding_idx 
    ON code_chunks USING hnsw (embedding vector_cosine_ops)
""")

// Search with cosine distance
var results = await conn.QueryAsync<CodeChunk>("""
    SELECT file_path, line_start, symbol_name, content, 
           embedding <=> @embedding as distance
    FROM code_chunks
    ORDER BY embedding <=> @embedding
    LIMIT @limit
""", new { embedding = new Vector(queryEmbedding), limit = 10 });
```

**Trade-offs:**
| Aspect | Qdrant | Azure AI Search | pgvector |
|--------|--------|-----------------|----------|
| Setup complexity | Low (Docker) | Medium (Azure) | Low (existing PG) |
| Hybrid search | Manual | Built-in RRF | Manual (separate FTS) |
| Performance at scale | Excellent | Excellent | Good (<1M vectors) |
| .NET integration | Excellent (official) | Excellent (official) | Good (Npgsql) |
| Cost | Free (self-host) | ~$0.10/1000 queries | Free (with PG) |
| Multi-tenancy | Payload filtering | Index-level | Schema-level |

---

## 2. Embedding Models for C# Code

### 2.1 Model Comparison for Code Retrieval

| Model | Provider | Dimensions | Context | Price/1M | Code Optimized | Free Tier |
|-------|----------|------------|---------|----------|----------------|-----------|
| **voyage-code-3** | Voyage AI | 1024 | 32K | $0.18 | Yes (best) | 200M tokens |
| **voyage-4-lite** | Voyage AI | 1024 | 32K | $0.02 | No | 200M tokens |
| **text-embedding-3-large** | OpenAI | 3072 | 8K | $0.13 | No | ~$5 trial |
| **text-embedding-3-small** | OpenAI | 1536 | 8K | $0.02 | No | ~$5 trial |
| **BGE-M3** | BAAI | 1024 | 8K | Free | Partial | Self-hosted |
| **Codestral Embed** | Mistral | 1536 | 32K | $0.15 | Yes | Rate-limited |
| **Gemini Embedding 2** | Google | 3072 | 8K | $0.20 | Yes (84 MTEB Code) | 1500 RPD |

### 2.2 voyage-code-3 (Recommended)

voyage-code-3 is the leading code-specialized embedding model [^128^][^122^]:

- **Performance**: Outperforms OpenAI text-embedding-3-large by 13.8% and CodeSage-large by 16.81% on 32 code retrieval datasets [^128^]
- **Dimensions**: 1024 (default), also supports 256, 512, 2048 via Matryoshka learning
- **Quantization**: float, int8, uint8, binary, ubinary for storage reduction
- **Context**: 32K tokens (4x OpenAI's 8K)
- **Pricing**: $0.18/1M tokens, with 200M free tokens per account [^398^]
- **input_type**: Supports `query` vs `document` types for asymmetric retrieval

```csharp
// Voyage AI API call
using System.Net.Http.Json;

async Task<float[]> GetEmbeddingAsync(string code, string model = "voyage-code-3")
{
    var request = new
    {
        input = new[] { code },
        model = model,
        input_type = "document"  // or "query" for search queries
    };

    var response = await httpClient.PostAsJsonAsync(
        "https://api.voyageai.com/v1/embeddings", request);
    var result = await response.Content.ReadFromJsonAsync<VoyageEmbeddingResponse>();
    return result!.Data[0].Embedding;
}
```

### 2.3 Using Microsoft.Extensions.AI for Abstraction

```csharp
using Microsoft.Extensions.AI;

// Register embedding generator
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
    new VoyageEmbeddingGenerator(
        new Uri("https://api.voyageai.com/v1/"),
        apiKey: Environment.GetEnvironmentVariable("VOYAGE_API_KEY")!,
        modelId: "voyage-code-3"));

// Use in service
public class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator)
{
    public async Task<ReadOnlyMemory<float>> EmbedCodeAsync(string code)
    {
        var embedding = await generator.GenerateAsync(code);
        return embedding.Vector;
    }
}
```

### 2.4 Self-Hosted Alternative: BGE-M3

For teams needing complete data privacy, BGE-M3 (MIT license) can be self-hosted [^488^]:

```python
# Self-hosted with sentence-transformers (for comparison)
from sentence_transformers import SentenceTransformer

model = SentenceTransformer('BAAI/bge-m3')
embeddings = model.encode(code_chunks, normalize_embeddings=True)
```

**Cost Analysis for 50K File Repository (FHIR server):**
- Estimated tokens: ~25M tokens (assuming ~500 lines avg, ~500 tokens/file)
- voyage-code-3: 25M tokens = $4.50 (within 200M free tier) [^398^]
- OpenAI text-embedding-3-large: 25M tokens = $3.25
- voyage-code-3 batch API: 25M tokens = $3.00 (33% discount)

---

## 3. AST-Based Code Chunking

### 3.1 Why AST-Based Chunking Outperforms Text Splitting

AST-based chunking parses code into its syntactic structure and splits at semantic boundaries (functions, classes, methods) rather than arbitrary character counts. This preserves code meaning and ensures complete logical units [^162^][^386^].

**Key advantages:**
- Never splits mid-function or mid-class [^385^]
- Preserves parent-child relationships (method → class → namespace)
- Enables metadata extraction (signature, docstring, parameters)
- Supports chunk expansion with metadata headers for better retrieval [^388^]

### 3.2 Tree-sitter for .NET (Multi-Language)

Tree-sitter is a battle-tested incremental parser used by GitHub, Neovim, and VS Code [^387^]. The `TreeSitter.DotNet` NuGet package provides official C# bindings [^384^][^390^].

**Installation:**
```bash
dotnet add package TreeSitter.DotNet --version 1.1.0
```

**Parse C# Code and Extract Functions:**
```csharp
using TreeSitter;

public class CodeChunker
{
    private readonly Parser _parser;

    public CodeChunker()
    {
        var language = new Language("CSharp");  // or "c_sharp"
        _parser = new Parser(language);
    }

    public List<CodeChunk> ChunkFile(string filePath, string sourceCode)
    {
        using var tree = _parser.Parse(sourceCode)!;
        var root = tree.RootNode;
        var chunks = new List<CodeChunk>();

        // Query to capture method declarations, class declarations, property declarations
        var query = new Query(new Language("CSharp"), """
            (method_declaration) @method
            (constructor_declaration) @constructor
            (property_declaration) @property
            (class_declaration) @class
            (interface_declaration) @interface
            (enum_declaration) @enum
        """);

        foreach (var match in query.Matches(root))
        {
            var node = match.Captures[0].Node;
            var chunk = new CodeChunk
            {
                Id = $"{filePath}#{node.StartPosition.Row}-{node.EndPosition.Row}",
                FilePath = filePath,
                StartLine = node.StartPosition.Row,
                EndLine = node.EndPosition.Row,
                SymbolType = node.Type,
                Content = node.Text,
                Language = "csharp"
            };

            // Extract parent class context
            var parent = FindParentClass(node);
            if (parent != null)
            {
                chunk.ParentClass = parent.Text;
                chunk.Content = $"// File: {filePath}\n// Class: {parent.Text}\n{chunk.Content}";
            }

            chunks.Add(chunk);
        }

        return chunks;
    }

    private static Node? FindParentClass(Node node)
    {
        var current = node.Parent;
        while (current != null)
        {
            if (current.Type == "class_declaration")
                return current;
            current = current.Parent;
        }
        return null;
    }
}

public class CodeChunk
{
    public string Id { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string SymbolType { get; set; } = "";
    public string SymbolName { get; set; } = "";
    public string? ParentClass { get; set; }
    public string Content { get; set; } = "";
    public string Language { get; set; } = "csharp";
    public int TokenCount { get; set; }
}
```

### 3.3 Roslyn for C# (Deep Semantic Analysis)

Roslyn provides the most powerful analysis for C# code, including semantic symbols, type resolution, and cross-reference analysis [^458^][^462^].

**Installation:**
```bash
dotnet add package Microsoft.CodeAnalysis.CSharp.Workspaces --version 4.11.0
dotnet add package Microsoft.CodeAnalysis.Workspaces.MSBuild --version 4.11.0
dotnet add package Microsoft.Build.Locator --version 1.7.0
```

**Parse Solution and Extract Symbols:**
```csharp
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

public class RoslynCodeAnalyzer
{
    public async Task<List<CodeChunk>> AnalyzeSolutionAsync(string solutionPath)
    {
        MSBuildLocator.RegisterDefaults();
        var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath);
        var chunks = new List<CodeChunk>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            foreach (var document in project.Documents)
            {
                if (!document.FilePath?.EndsWith(".cs") ?? true) continue;

                var syntaxTree = await document.GetSyntaxTreeAsync();
                var semanticModel = compilation?.GetSemanticModel(syntaxTree!);
                var root = await syntaxTree!.GetRootAsync();

                // Extract all method declarations
                var methods = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                foreach (var method in methods)
                {
                    var symbol = semanticModel?.GetDeclaredSymbol(method);
                    var lineSpan = syntaxTree.GetLineSpan(method.Span);

                    var chunk = new CodeChunk
                    {
                        Id = $"{document.FilePath}#{method.Identifier.Text}",
                        FilePath = document.FilePath!,
                        StartLine = lineSpan.StartLinePosition.Line + 1,
                        EndLine = lineSpan.EndLinePosition.Line + 1,
                        SymbolType = "method",
                        SymbolName = method.Identifier.Text,
                        ParentClass = symbol?.ContainingType?.Name,
                        Signature = method.ToString()[..Math.Min(200, method.ToString().Length)],
                        Content = method.ToFullString(),
                        Language = "csharp",
                        ReturnType = method.ReturnType.ToString(),
                        Parameters = method.ParameterList.Parameters
                            .Select(p => $"{p.Type} {p.Identifier}")
                            .ToList(),
                        Usings = root.DescendantNodes()
                            .OfType<UsingDirectiveSyntax>()
                            .Select(u => u.Name?.ToString())
                            .Where(u => u != null)
                            .ToList()!
                    };

                    chunks.Add(chunk);
                }
            }
        }

        return chunks;
    }
}
```

### 3.4 Recommended Hybrid Approach

For the best of both worlds, combine tree-sitter (fast, multi-language) with Roslyn (deep C# semantics):

```
Chunking Pipeline:
1. Tree-sitter: Initial parse, extract boundaries (methods, classes)
2. Roslyn (C# only): Enrich with semantic metadata (symbol types, usings, references)
3. Metadata header prepending: Add "File: X | Class: Y | Method: Z" prefix
4. Token counting: Check against max_chunk_size, split if needed
5. Embedding: voyage-code-3 with input_type="document"
```

**Chunk Expansion with Metadata Header (from ASTChunk research) [^162^]:**
```csharp
string ExpandChunkWithMetadata(CodeChunk chunk)
{
    return $"""
        // File: {chunk.FilePath}
        // Symbol: {chunk.SymbolType} {chunk.SymbolName}
        // Parent: {chunk.ParentClass ?? "global"}
        // Lines: {chunk.StartLine}-{chunk.EndLine}
        {chunk.Content}
    """;
}
```

---

## 4. Incremental Git-Based Indexing

### 4.1 Change Detection Strategy

The incremental indexing pipeline detects changes via SHA256 content hashing and git diff [^493^][^383^].

**Classification Rules:**
| State | Detection Action |
|-------|-----------------|
| Path same + hash same | No change (reuse existing embedding) |
| Path same + hash different | Modified (re-chunk + re-embed) |
| New path | Added (chunk + embed) |
| Old path missing | Deleted (remove from index) |
| Path changed | Treat as delete old + add new |

### 4.2 Git-Based Change Detection Implementation

```csharp
using System.Security.Cryptography;
using System.Text;

public class GitChangeDetector
{
    private readonly string _repoPath;
    private readonly Dictionary<string, string> _fileHashes;  // persisted

    public async Task<ChangeSet> DetectChangesAsync(string commitHash)
    {
        var changes = new ChangeSet();
        var currentFiles = await GetTrackedFilesAsync(commitHash);

        foreach (var file in currentFiles)
        {
            var hash = await ComputeFileHashAsync(file);
            var relativePath = Path.GetRelativePath(_repoPath, file);

            if (!_fileHashes.TryGetValue(relativePath, out var oldHash))
            {
                changes.Added.Add(relativePath);
                _fileHashes[relativePath] = hash;
            }
            else if (oldHash != hash)
            {
                changes.Modified.Add(relativePath);
                _fileHashes[relativePath] = hash;
            }
        }

        // Detect deletions
        foreach (var oldPath in _fileHashes.Keys.Except(currentFiles.Select(
            f => Path.GetRelativePath(_repoPath, f))))
        {
            changes.Deleted.Add(oldPath);
            _fileHashes.Remove(oldPath);
        }

        return changes;
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}

public class ChangeSet
{
    public List<string> Added { get; } = new();
    public List<string> Modified { get; } = new();
    public List<string> Deleted { get; } = new();
}
```

### 4.3 Incremental Indexing Pipeline

```csharp
public class IncrementalIndexer
{
    private readonly GitChangeDetector _changeDetector;
    private readonly CodeChunker _chunker;
    private readonly EmbeddingService _embedder;
    private readonly QdrantClient _vectorDb;

    public async Task UpdateIndexAsync(string commitHash)
    {
        var changes = await _changeDetector.DetectChangesAsync(commitHash);

        // Process deletions first
        foreach (var file in changes.Deleted)
        {
            await DeleteByFilePathAsync(file);
        }

        // Process additions and modifications
        var filesToProcess = changes.Added.Concat(changes.Modified);
        foreach (var file in filesToProcess)
        {
            // Delete old chunks for modified files
            if (changes.Modified.Contains(file))
                await DeleteByFilePathAsync(file);

            // Parse and chunk
            var sourceCode = await File.ReadAllTextAsync(file);
            var chunks = _chunker.ChunkFile(file, sourceCode);

            // Embed and upsert in batches
            var batch = new List<PointStruct>();
            foreach (var chunk in chunks)
            {
                var embedding = await _embedder.EmbedAsync(chunk.Content);
                batch.Add(new PointStruct
                {
                    Id = (ulong)chunk.Id.GetHashCode(),
                    Vectors = new Dictionary<string, float[]>
                        { ["code_embedding"] = embedding },
                    Payload = { ["file_path"] = file, ["content"] = chunk.Content }
                });

                if (batch.Count >= 100)
                {
                    await _vectorDb.UpsertAsync("code_chunks", batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await _vectorDb.UpsertAsync("code_chunks", batch);
        }
    }
}
```

---

## 5. Code Chunk Metadata Structure

### 5.1 Recommended Payload Schema

Each code chunk should carry rich metadata to enable precise filtering and context assembly:

```json
{
  "id": "src/Services/PatientService.cs#GetPatientByIdAsync-42-89",
  "file_path": "src/Services/PatientService.cs",
  "file_name": "PatientService.cs",
  "directory": "src/Services",
  "repo_name": "fhir-server",
  "branch": "main",
  "commit_hash": "a1b2c3d4",
  "start_line": 42,
  "end_line": 89,
  "symbol_type": "method",
  "symbol_name": "GetPatientByIdAsync",
  "parent_class": "PatientService",
  "parent_namespace": "Microsoft.Health.Fhir.Core.Services",
  "signature": "public async Task<Patient> GetPatientByIdAsync(string id, CancellationToken ct)",
  "return_type": "Task<Patient>",
  "parameters": ["string id", "CancellationToken ct"],
  "language": "csharp",
  "content": "public async Task<Patient> GetPatientByIdAsync(...) { ... }",
  "usings": ["System", "System.Threading", "Hl7.Fhir.Model"],
  "doc_comment": "/// <summary>Retrieves a patient by ID</summary>",
  "token_count": 156,
  "last_modified": "2025-01-15T10:30:00Z"
}
```

### 5.2 Qdrant Payload Configuration

```csharp
// Create collection with payload indexes for efficient filtering
await client.CreateCollectionAsync("code_chunks",
    vectorsConfig: new VectorParamsMap
    {
        Map = {
            ["code_embedding"] = new VectorParams {
                Size = 1024, Distance = Distance.Cosine }
        }
    });

// Create payload indexes for filtering
await client.CreatePayloadIndexAsync("code_chunks", "file_path",
    new PayloadSchemaType { Type = PayloadSchemaType.Keyword });
await client.CreatePayloadIndexAsync("code_chunks", "symbol_type",
    new PayloadSchemaType { Type = PayloadSchemaType.Keyword });
await client.CreatePayloadIndexAsync("code_chunks", "language",
    new PayloadSchemaType { Type = PayloadSchemaType.Keyword });
await client.CreatePayloadIndexAsync("code_chunks", "parent_class",
    new PayloadSchemaType { Type = PayloadSchemaType.Keyword });
```

---

## 6. Hybrid Search Implementation

### 6.1 Option A: Azure AI Search (Built-in Hybrid)

Azure AI Search provides the simplest hybrid search implementation with Reciprocal Rank Fusion (RRF) [^359^][^361^].

```csharp
// Hybrid search: text query runs BM25, vector query runs ANN,
// results merged via RRF
var options = new SearchOptions
{
    VectorSearch = new VectorSearchOptions
    {
        Queries = {
            new VectorizedQuery(queryEmbedding)
            {
                KNearestNeighborsCount = 20,
                Fields = { "content_vector" },
                // Boost vector score weight
                Weight = 0.7
            }
        }
    },
    Size = 10,
    Select = { "file_path", "content", "symbol_name", "start_line", "end_line" }
};

// The text query parameter triggers BM25 search on searchable fields
var results = await searchClient.SearchAsync<CodeChunk>(
    "patient authentication JWT", options);
```

**RRF Formula**: `score = SUM(1 / (rank_i + k))` where k is a constant (default 60), and rank_i is the document's rank from each search subsystem [^360^].

### 6.2 Option B: Qdrant + Custom BM25

For Qdrant, implement hybrid search by running parallel queries and merging:

```csharp
public class HybridSearchService
{
    private readonly QdrantClient _qdrant;
    private readonly BM25Index _bm25;  // Custom or Lucene.NET

    public async Task<List<ScoredResult>> HybridSearchAsync(
        string queryText, float[] queryVector, int topK = 10)
    {
        // Parallel execution
        var vectorTask = _qdrant.SearchAsync("code_chunks", queryVector,
            limit: topK * 2);
        var bm25Task = _bm25.SearchAsync(queryText, topK * 2);

        await Task.WhenAll(vectorTask, bm25Task);

        // RRF merging
        var rrfScores = new Dictionary<string, double>();
        const int k = 60;

        void MergeResults(IEnumerable<(string id, int rank)> results, double weight)
        {
            foreach (var (id, rank) in results.Select((r, i) => (r.id, i + 1)))
            {
                if (!rrfScores.ContainsKey(id))
                    rrfScores[id] = 0;
                rrfScores[id] += weight * (1.0 / (rank + k));
            }
        }

        MergeResults(vectorTask.Result.Select(r =>
            (r.Id.ToString(), r.Score)), 0.7);
        MergeResults(bm25Task.Result.Select(r =>
            (r.Id, r.Score)), 0.3);

        return rrfScores.OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => new ScoredResult { Id = kv.Key, Score = kv.Value })
            .ToList();
    }
}
```

### 6.3 Option C: pgvector + PostgreSQL Full-Text Search

```sql
-- Combined vector + full-text hybrid query
SELECT
    id,
    file_path,
    content,
    symbol_name,
    -- Cosine similarity for vector
    1 - (embedding <=> $1) as vector_score,
    -- BM25-like ts_rank for text
    ts_rank_cd(to_tsvector('english', content), query) as text_score,
    -- Combined score (weighted)
    (0.7 * (1 - (embedding <=> $1))) +
    (0.3 * ts_rank_cd(to_tsvector('english', content), query)) as hybrid_score
FROM code_chunks, plainto_tsquery('english', $2) query
WHERE to_tsvector('english', content) @@ query
ORDER BY hybrid_score DESC
LIMIT 10;
```

---

## 7. Code Indexing Service Architecture

### 7.1 System Architecture

```
                    +---------------------+
                    |   GitHub Webhook    |
                    |   (push/PR merge)   |
                    +----------+----------+
                               |
                    +----------v----------+
                    |  Git Change Detector|
                    |  (SHA256 hashing)   |
                    +----------+----------+
                               |
              +----------------+----------------+
              |                                 |
    +---------v---------+           +-----------v--------+
    |  Full Indexer     |           |  Incremental       |
    |  (initial clone)  |           |  Indexer (diff)    |
    +---------+---------+           +-----------+--------+
              |                                 |
              +----------------+----------------+
                               |
                    +----------v----------+
                    |  Code Chunker       |
                    |  Tree-sitter +      |
                    |  Roslyn enrich      |
                    +----------+----------+
                               |
                    +----------v----------+
                    |  Embedding Service  |
                    |  (voyage-code-3)    |
                    +----------+----------+
                               |
                    +----------v----------+
                    |  Vector Database    |
                    |  (Qdrant/PG)        |
                    +---------------------+
                               |
                    +----------v----------+
                    |  Query API (REST)   |
                    |  /search /query     |
                    +---------------------+
```

### 7.2 Service Components (ASP.NET Core)

```csharp
// Program.cs - Service composition
var builder = WebApplication.CreateBuilder(args);

// Vector database
builder.Services.AddSingleton<QdrantClient>(sp =>
    new QdrantClient(builder.Configuration["Qdrant:Host"]!));

// Chunkers
builder.Services.AddSingleton<CodeChunker>();       // Tree-sitter
builder.Services.AddSingleton<RoslynCodeAnalyzer>(); // Roslyn (C# deep)

// Embedding
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
    new VoyageEmbeddingGenerator(
        new Uri("https://api.voyageai.com/v1/"),
        apiKey: builder.Configuration["Voyage:ApiKey"]!,
        modelId: "voyage-code-3"));

// Indexers
builder.Services.AddSingleton<GitChangeDetector>();
builder.Services.AddSingleton<IncrementalIndexer>();
builder.Services.AddSingleton<HybridSearchService>();

var app = builder.Build();

// Indexing endpoint
app.MapPost("/api/index/{repo}", async (
    string repo, IndexRequest request, IncrementalIndexer indexer) =>
{
    await indexer.IndexRepositoryAsync(repo, request.Branch, request.CommitHash);
    return Results.Ok(new { indexed = true, repo, request.CommitHash });
});

// Search endpoint for agents
app.MapPost("/api/search", async (
    SearchRequest request, HybridSearchService search) =>
{
    var results = await search.HybridSearchAsync(
        request.Query, request.Filters, request.TopK);
    return Results.Ok(results);
});

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
```

### 7.3 Background Worker for Indexing

```csharp
public class CodeIndexingWorker : BackgroundService
{
    private readonly IncrementalIndexer _indexer;
    private readonly ILogger<CodeIndexingWorker> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll for new commits or process webhook queue
                await _indexer.ProcessPendingChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Indexing failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

---

## 8. Agent Query API Design

### 8.1 REST API Specification

```yaml
openapi: 3.0.0
paths:
  /api/search:
    post:
      summary: Search indexed code
      requestBody:
        content:
          application/json:
            schema:
              type: object
              properties:
                query:
                  type: string
                  description: Natural language or code query
                  example: "How is patient authentication handled?"
                top_k:
                  type: integer
                  default: 10
                filters:
                  type: object
                  properties:
                    file_path:
                      type: string
                      example: "src/Services/*.cs"
                    symbol_type:
                      type: array
                      items: string
                      example: ["method", "class"]
                    language:
                      type: string
                      example: "csharp"
                include_content:
                  type: boolean
                  default: true
                hybrid:
                  type: boolean
                  default: true
      responses:
        200:
          description: Search results
          content:
            application/json:
              schema:
                type: object
                properties:
                  results:
                    type: array
                    items:
                      type: object
                      properties:
                        id: string
                        file_path: string
                        start_line: integer
                        end_line: integer
                        symbol_name: string
                        parent_class: string
                        score: number
                        content: string
                        signature: string
  /api/semantic-query:
    post:
      summary: Query with semantic + graph expansion
      requestBody:
        content:
          application/json:
            schema:
              type: object
              properties:
                query:
                  type: string
                expand_graph:
                  type: boolean
                  description: Include related symbols via call graph
                  default: false
```

### 8.2 Semantic Kernel Integration

```csharp
// Register as a plugin for Semantic Kernel
public class CodeSearchPlugin
{
    private readonly HybridSearchService _search;

    [KernelFunction("search_code")]
    [Description("Search the codebase for relevant code given a query")]
    public async Task<string> SearchCodeAsync(
        [Description("The search query, can be natural language or code terms")]
        string query,
        [Description("Optional file path filter")]
        string? fileFilter = null)
    {
        var results = await _search.HybridSearchAsync(query,
            fileFilter != null ? new { file_path = fileFilter } : null);

        return string.Join("\n---\n", results.Select(r =>
            $"File: {r.FilePath}:{r.StartLine}-{r.EndLine}\n" +
            $"Symbol: {r.SymbolName} (class: {r.ParentClass})\n" +
            $"```csharp\n{r.Content}\n```"));
    }
}

// Register plugin
builder.Services.AddSingleton<CodeSearchPlugin>();
var kernel = builder.Services.BuildServiceProvider()
    .GetRequiredService<Kernel>();
kernel.ImportPluginFromType<CodeSearchPlugin>();
```

### 8.3 Response Format for Agent Consumption

```json
{
  "query": "patient authentication JWT validation",
  "total_results": 10,
  "search_time_ms": 45,
  "results": [
    {
      "rank": 1,
      "score": 0.923,
      "id": "src/Auth/JwtValidator.cs#ValidateToken-28-67",
      "file_path": "src/Auth/JwtValidator.cs",
      "file_name": "JwtValidator.cs",
      "start_line": 28,
      "end_line": 67,
      "symbol_type": "method",
      "symbol_name": "ValidateToken",
      "parent_class": "JwtValidator",
      "parent_namespace": "Microsoft.Health.Fhir.Auth",
      "signature": "public ClaimsPrincipal ValidateToken(string token, TokenValidationParameters parameters)",
      "return_type": "ClaimsPrincipal",
      "parameters": ["string token", "TokenValidationParameters parameters"],
      "content": "public ClaimsPrincipal ValidateToken(...) { ... }",
      "usings": ["System.IdentityModel.Tokens.Jwt", "Microsoft.IdentityModel.Tokens"],
      "doc_comment": "Validates a JWT bearer token"
    }
  ]
}
```

---

## 9. Token & Cost Estimates

### 9.1 Token Estimation for Code

For code, token estimation follows these heuristics [^489^]:
- **~4 characters per token** (rough estimate for code)
- **~1.3 tokens per line** of C# code (averaged)

### 9.2 FHIR Server Repository (~50K files) Estimate

| Metric | Estimate |
|--------|----------|
| Total files | 50,000 |
| Avg lines per file | 200 |
| Total lines of code | ~10,000,000 |
| Estimated tokens (content) | ~13,000,000 tokens |
| Estimated tokens (with metadata headers) | ~20,000,000 tokens |
| Chunk count (~50 lines avg chunk) | ~200,000 chunks |
| Embedding API calls (batch size 128) | ~1,563 calls |

### 9.3 Cost Breakdown

| Model | Cost for 20M tokens | Free Tier | Notes |
|-------|---------------------|-----------|-------|
| **voyage-code-3** | $3.60 | 200M tokens | **Recommended** |
| voyage-code-3 (batch API, -33%) | $2.40 | 200M tokens | Best for large initial index |
| voyage-4-lite | $0.40 | 200M tokens | Budget option |
| OpenAI text-embedding-3-large | $2.60 | ~$5 trial | General purpose |
| OpenAI text-embedding-3-small | $0.40 | ~$5 trial | Lower quality |
| BGE-M3 (self-hosted) | $0 (GPU cost) | N/A | Requires GPU infra |

**Storage Estimate:**
- 200K chunks x 1024 dimensions x 4 bytes/float = ~819 MB raw vector storage
- With Qdrant HNSW index overhead: ~1.5-2 GB
- With int8 quantization: ~400 MB

### 9.4 Incremental Update Costs

| Scenario | Files Changed | Tokens | Cost (voyage-code-3) |
|----------|--------------|--------|---------------------|
| Small PR | 5 files, 200 lines | ~1,300 | $0.0002 (negligible) |
| Medium PR | 50 files, 2K lines | ~13,000 | $0.002 (negligible) |
| Large feature | 200 files, 10K lines | ~65,000 | $0.01 (negligible) |
| Full reindex | 50K files | ~20M | $3.60 |

---

## 10. Keeping the Index Fresh

### 10.1 Webhook-Driven Updates (Recommended)

Configure a GitHub webhook to trigger reindexing on push/PR merge:

```csharp
// ASP.NET Core webhook handler
app.MapPost("/api/webhooks/github", async (
    HttpRequest request, IncrementalIndexer indexer) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync();
    var eventType = request.Headers["X-GitHub-Event"];

    if (eventType == "push")
    {
        var pushEvent = JsonSerializer.Deserialize<PushEvent>(payload)!;
        await indexer.UpdateIndexAsync(
            pushEvent.Repository.FullName,
            pushEvent.Ref,
            pushEvent.After);  // new commit hash
    }

    return Results.Ok();
});
```

### 10.2 Polling Strategy

For repositories where webhooks aren't available:

```csharp
public class GitPollingService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var latestCommit = await GetLatestCommitAsync();
            if (latestCommit != _lastIndexedCommit)
            {
                await _indexer.UpdateIndexAsync(latestCommit);
                _lastIndexedCommit = latestCommit;
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

### 10.3 Branch-Based Index Isolation

Using branch-based isolation for safe index updates [^407^]:

```csharp
// Create isolated collection per branch
var collectionName = $"code_chunks_{branch.Replace("/", "_")}";

// Index new branch
await _indexer.IndexRepositoryAsync(repo, branch, commitHash, collectionName);

// Atomically swap (for production)
if (branch == "main")
{
    await _qdrant.UpdateCollectionAsync("code_chunks_production",
        new UpdateCollection { ... });  // point to new snapshot
}
```

### 10.4 Update Frequency Recommendations

| Environment | Trigger | Latency |
|-------------|---------|---------|
| Development | Manual (on demand) | Immediate |
| Staging | PR merge webhook | ~30 seconds |
| Production | Release tag webhook | ~2 minutes |
| Multi-tenant | Per-tenant polling | ~5 minutes |

---

## Limitations & Gotchas

1. **Roslyn MSBuildWorkspace requires buildable solution**: Roslyn needs to resolve all references. Partial or broken solutions may fail to load [^458^][^466^].

2. **Tree-sitter C# grammar may lag behind language features**: New C# language features may not be supported immediately in tree-sitter grammars.

3. **voyage-code-3 API has rate limits**: 200M free tokens, then $0.18/1M. Batch API gives 33% discount but 12-hour SLA [^398^].

4. **Qdrant HNSW index build time**: Large collections may take minutes to build HNSW indexes. Use `on_disk` option for large collections.

5. **Git LFS files**: Binary files tracked via Git LFS should be excluded from indexing.

6. **Embedding dimension lock-in**: Changing embedding models requires full reindex. Plan for model versioning from day one [^411^].

7. **Code comments vs implementation**: Include doc comments in chunks for better retrieval, but weigh appropriately to avoid matching on comments alone.

8. **Cross-file references**: Pure vector search won't capture "call graph" relationships. Consider GraphRAG (Neo4j) for advanced use cases [^402^][^410^].

---

## Recommendations for Prototype

### Technology Stack

| Component | Technology | Reason |
|-----------|-----------|--------|
| Vector DB | **Qdrant** (Docker) | Native .NET client, Aspire support, open-source |
| Embeddings | **voyage-code-3** | Best code retrieval, 200M free tokens |
| Chunking | **TreeSitter.DotNet** + **Roslyn** | Multi-language + deep C# semantics |
| API | **ASP.NET Core Minimal APIs** | Simple, fast, native |
| Hosting | **.NET Aspire** | Service orchestration, dev/prod parity |
| Search | **Hybrid (vector + keyword)** | Best of both semantic and exact matching |

### Implementation Order

1. **Phase 1 (Week 1)**: Docker Qdrant + voyage-code-3 + tree-sitter chunking + basic search API
2. **Phase 2 (Week 2)**: Add Roslyn enrichment for C# + incremental git indexing
3. **Phase 3 (Week 3)**: Hybrid search + metadata filtering + webhook triggers
4. **Phase 4 (Week 4)**: GraphRAG (Neo4j) for cross-reference analysis + production hardening

### Minimum Viable Configuration

```yaml
# docker-compose.yml for local development
version: '3.8'
services:
  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"
      - "6334:6334"
    volumes:
      - qdrant_storage:/qdrant/storage

  code-indexer:
    build: .
    environment:
      - Qdrant__Host=qdrant
      - Voyage__ApiKey=${VOYAGE_API_KEY}
    depends_on:
      - qdrant

volumes:
  qdrant_storage:
```

---

## Sources & References

[^122^] Voyage AI Documentation - Text Embeddings Introduction. https://docs.voyageai.com/docs/embeddings

[^128^] Voyage AI Blog - voyage-code-3 announcement. https://blog.voyageai.com/2024/12/04/voyage-code-3/

[^151^] Supermemory Blog - Building code-chunk: AST Aware Code Chunking. https://supermemory.ai/blog/building-code-chunk-ast-aware-code-chunking/

[^162^] Databricks Blog - Building a Knowledge Assistant over Code. https://www.databricks.com/blog/building-knowledge-assistant-over-code

[^359^] Microsoft Docs - Create a hybrid query in Azure AI Search. https://docs.azure.cn/en-us/search/hybrid-search-how-to-query

[^360^] Microsoft Learn - Quickstart: Vector Search in Azure AI Search. https://learn.microsoft.com/en-us/azure/search/search-get-started-vector

[^361^] Medium - Implement Hybrid Search with Azure AI Search (.NET SDK). https://medium.com/@swati.satpathy/implement-hybrid-search-with-azure-ai-search-net-sdk-466fa1f8c974

[^383^] CocoIndex - Incremental engine for long horizon agents. https://github.com/cocoindex-io/cocoindex

[^384^] NuGet - TreeSitter.DotNet 1.1.0. https://www.nuget.org/packages/TreeSitter.DotNet/1.1.0

[^385^] GitHub - NousResearch: AST-aware code chunking via tree-sitter. https://github.com/NousResearch/hermes-agent/issues/5854

[^386^] PyPI - treesitter-chunker. https://pypi.org/p/treesitter-chunker/

[^387^] Tree-sitter Official Website. https://tree-sitter.github.io/

[^388^] GitHub - ASTChunk: AST-based code chunking toolkit. https://github.com/yilinjz/astchunk

[^390^] GitHub - tree-sitter-dotnet-bindings. https://github.com/mariusgreuel/tree-sitter-dotnet-bindings

[^393^] GitHub - ms-graphrag-neo4j. https://github.com/neo4j-contrib/ms-graphrag-neo4j

[^397^] Awesome Agents - Embedding Models Pricing (April 2026). https://awesomeagents.ai/pricing/embedding-models-pricing/

[^398^] Voyage AI - Pricing Documentation. https://docs.voyageai.com/docs/pricing

[^401^] DZone - Vector Databases in Action: RAG Pipeline for Code Search. https://dzone.com/articles/vector-databases-rag-pipeline-code-search

[^402^] Bytebell - Graph RAG Strategy for Multi-Repository Code Changes. https://bytebell.ai/blog/simple-graph-rag

[^405^] .NET Aspire - Qdrant Integration. https://aspire.dev/integrations/databases/qdrant/qdrant-get-started/

[^407^] lakeFS - RAG Pipeline: Example, Tools & How to Build It. https://lakefs.io/blog/what-is-rag-pipeline/

[^408^] LiteLLM - Voyage AI Provider Documentation. https://docs.litellm.ai/docs/providers/voyage

[^410^] arXiv - LogicLens: Leveraging Semantic Code Graph. https://arxiv.org/html/2601.10773v1

[^411^] n8n Blog - RAG System Architecture: Production Implementation Guide. https://blog.n8n.io/rag-system-architecture/

[^412^] GitHub - catsu embedding models reference. https://github.com/chonkie-inc/catsu/blob/main/DOCS.md

[^413^] Microsoft DevBlogs - Qdrant and Azure AI Search with VectorData. https://devblogs.microsoft.com/dotnet/vector-data-qdrant-ai-search-dotnet/

[^415^] Forloop Blog - Using Qdrant for Embeddings Search with C#. https://forloop.co.uk/blog/using-qdrant-for-embeddings-search-with-csharp

[^419^] Pinecone Blog - Full Text Search Architecture. https://www.pinecone.io/blog/full-text-search-architecture/

[^420^] Understanding BM25 algorithm. https://emschwartz.me/understanding-the-bm25-full-text-search-algorithm/

[^422^] Neo4j Documentation - User Guide: RAG. https://neo4j.com/docs/neo4j-graphrag-python/current/user_guide_rag.html

[^423^] Medium - Semantic GraphRAG Implementation Guide with Neo4j, Qdrant. https://medium.com/@visrow/semantic-graphrag-implementation-guide-build-real-world-ai-knowledge-systems-with-neo4j-qdrant-9d272d2f99c4

[^424^] OpenAI Embeddings Pricing Calculator. https://invertedstone.com/calculators/embedding-pricing-calculator

[^425^] Stack Overflow - Extract called method information using Roslyn. https://stackoverflow.com/questions/55118805/extract-called-method-information-using-roslyn

[^428^] Token Counter. https://www.quizrise.com/token-counter

[^429^] Habr - Creating Roslyn API-based static analyzer for C#. https://habr.com/en/companies/pvs-studio/articles/579736/

[^431^] GitHub - pg_textsearch PostgreSQL extension for BM25. https://github.com/timescale/pg_textsearch

[^457^] DBI Services - RAG Series: Agentic RAG. https://www.dbi-services.com/blog/rag-series-agentic-rag/

[^458^] Meziantou - Using Roslyn to analyze and rewrite code. https://www.meziantou.net/using-roslyn-to-analyze-and-rewrite-code-in-a-solution.htm

[^459^] Microsoft DevBlogs - Semantic Search with Microsoft.Extensions.VectorData. https://devblogs.microsoft.com/dotnet/vector-data-qdrant-ai-search-dotnet/

[^462^] Steve Gordon - Using Roslyn APIs to analyse a .NET Solution. https://www.stevejgordon.co.uk/using-the-roslyn-apis-to-analyse-a-dotnet-solution

[^464^] Rajeev Pentyala - Build a RAG Chat App in C# with Semantic Kernel, Ollama, Qdrant. https://rajeevpentyala.com/2025/07/29/build-a-rag-chat-app-in-c-using-semantic-kernel-ollama-and-qdrant/

[^467^] DevLeader - Semantic Kernel Vector Store in C#. https://www.devleader.ca/2026/03/14/semantic-kernel-vector-store-in-c-azure-ai-search-qdrant-and-beyond

[^471^] Microsoft Learn - Work with .NET Compiler Platform SDK workspace. https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-workspace

[^486^] arXiv - CoIR: Comprehensive Benchmark for Code Information Retrieval. https://arxiv.org/html/2407.02883v3

[^487^] AI Log - MTEB Scores & Leaderboard. https://app.ailog.fr/en/blog/guides/choosing-embedding-models

[^488^] PE Collective - Best Embedding Models 2026. https://pecollective.com/tools/best-embedding-models/

[^489^] promptprep - Token Counting Documentation. https://promptprep.readthedocs.io/en/stable/token_counting.html

[^490^] Voyage AI - Code Retrieval Evaluation. https://blog.voyageai.com/2024/12/04/code-retrieval-eval/

[^493^] GitHub - OpenViking Incremental Resource Update RFC. https://github.com/volcengine/OpenViking/discussions/380
