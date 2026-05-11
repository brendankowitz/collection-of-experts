## Facet: Code Indexing & RAG for Repository Understanding

### Key Findings

- **Hybrid search (BM25 + dense vector) is table stakes for code RAG**: Every major production system (Claude Context, Cursor, Roo Code) uses hybrid search because code queries involve both exact identifiers (function names, error codes) and semantic concepts. Pure vector search misses exact matches; pure keyword search misses semantic relationships. [^63^] [^207^]

- **AST-based chunking dramatically outperforms naive text splitting**: The cAST paper and production tools (Cursor, Windsurf, Claude Context) demonstrate that splitting at function/class boundaries via tree-sitter preserves syntactic integrity and improves retrieval quality. Naive chunking splits functions mid-definition, losing critical context. [^28^] [^151^]

- **Merkle trees are the standard for incremental indexing**: Cursor, Claude Context, and Semantic Context MCP all use Merkle trees for efficient change detection, enabling re-indexing of only modified files rather than full rebuilds. This reduces embedding API costs and indexing time from minutes to milliseconds for no-change scenarios. [^63^] [^61^] [^65^]

- **voyage-code-3 is the current gold standard for code embeddings**: It outperforms OpenAI text-embedding-3-large by 13.80% and CodeSage-large by 16.81% across 32 code retrieval datasets, supports 32K context, 300+ languages, and offers multiple quantization formats (float, int8, binary) for storage optimization. [^128^] [^126^]

- **MCP (Model Context Protocol) has emerged as the standard interface for agent-codebase integration**: Claude Context, code-index-mcp, and semantic-context-mcp all expose code indexing services via MCP servers, allowing any MCP-compatible agent (Claude Code, Cursor, Roo Code) to discover and query indexed codebases dynamically. [^5^] [^207^] [^254^]

- **GraphRAG for code captures dependency relationships that vector search misses**: Research shows that function-call dependency graphs combined with vector search (hybrid GraphRAG) significantly improve repository-level code generation, achieving pass@1 scores of 36.36% vs. pure RAG baselines. Knowledge graphs store CALLS, IMPORTS, EXTENDS, and DEFINES relationships. [^206^] [^261^] [^252^]

- **Cursor's architecture with Turbopuffer demonstrates billion-scale code indexing**: Cursor stores 1T+ vectors across namespaces (one per codebase), using object storage (S3) for cold data and NVMe for hot data, achieving 20x cost reduction vs. traditional vector databases. Custom embeddings + a fine-tuned 7B CodeLlama reranker process up to 500K tokens per query. [^68^] [^66^]

- **Claude Context achieves ~40% token reduction** in controlled benchmarks vs. grep-based retrieval by using hybrid (BM25 + dense) search with Milvus, cutting both LLM token consumption and tool calls. Indexing VS Code's 1.5M-line codebase costs ~$1.06 in embeddings. [^63^] [^5^]

- **Multiple vector databases are viable but trade off along latency, scale, and ops complexity**: Qdrant excels at single-node performance (<50ms p99); Milvus scales to billions with GPU acceleration; Turbopuffer optimizes cost at scale via S3-backed architecture; pgvector works well for <50M vectors in existing PostgreSQL deployments. [^31^] [^33^] [^35^]

---

### Major Players & Sources

| Entity | Role/Relevance |
|--------|---------------|
| **Zilliz / Milvus** | Open-source vector database powering Claude Context. Supports hybrid BM25+dense search via `SPARSE_FLOAT_VECTOR`. Milvus 2.6 adds tiered storage, 1-bit quantization (72% memory reduction), and built-in BM25. Zilliz Cloud offers managed Milvus. [^258^] [^63^] |
| **Qdrant** | Rust-based vector database used by Roo Code. Excels at single-node performance, payload filtering, and offers a generous free tier. Written in Rust with SIMD optimizations. [^21^] [^34^] |
| **Turbopuffer** | Serverless vector+full-text search engine used by Cursor. S3-backed architecture with unlimited namespaces enables 20x cost reduction. Namespace-per-codebase design scales to trillions of vectors. [^68^] [^64^] |
| **Voyage AI** | Provider of voyage-code-3, the leading code-specific embedding model. 32K context, 300+ languages, quantization-aware training (binary at 256D still outperforms OpenAI v3-large). [^128^] [^122^] |
| **OpenAI** | text-embedding-3-small ($0.02/M tokens) is the cost-effective default for general code. text-embedding-3-large ($0.13/M tokens) for higher quality. Used by Claude Context, Cursor (optionally), and many open-source tools. [^231^] |
| **Cursor** | Pioneer of codebase indexing in AI IDEs. Uses Turbopuffer + custom embeddings + Merkle tree sync + AST chunking + 7B CodeLlama reranker. Architecture widely documented and emulated. [^61^] [^66^] [^68^] |
| **Roo Code** | Open-source VS Code extension using Qdrant for codebase indexing. Supports multiple embedding providers (OpenAI, Gemini, Ollama). Tree-sitter parsing for semantic blocks. [^21^] [^124^] |
| **Continue.dev** | Uses LanceDB (embedded TypeScript vector DB) for local-first codebase retrieval. No separate process required. Voyage AI code embeddings for semantic search. [^158^] |
| **GitHub Copilot** | Uses proprietary code-tuned transformer embeddings. Hybrid approach: remote semantic index (GitHub cloud) + local index (<750 files via SQLite). Multi-tiered search: remote semantic → local embeddings → TF-IDF fallback. [^152^] [^153^] |
| **Sourcegraph** | Code Intelligence Platform using SCIP (code graph data) for precise cross-repository navigation. Auto-indexing with language-specific indexers. "Compiler-accurate" navigation vs. heuristic-based approaches. [^212^] [^215^] |
| **Semantic Context MCP / code-index-mcp** | Open-source MCP servers for code indexing. Use Merkle trees, ChromaDB, and ctags respectively. Demonstrate the emerging MCP-first pattern for agent-codebase integration. [^65^] [^254^] |

---

### Trends & Signals

- **MCP servers becoming the de facto standard for agent-codebase integration**: Multiple tools (Claude Context, code-index-mcp, semantic-context-mcp, mcp-server-milvus) now expose code indexing as MCP servers, enabling any MCP-compatible agent to query codebases without tool-specific integration. [^207^] [^255^]

- **Code-specific embedding models pulling ahead of general-purpose models**: voyage-code-3 (13.8% better than OpenAI on code), jina-code-embeddings (state-of-the-art on MTEB-CoIR), and CodeSage demonstrate that code-specific training data and structural understanding yield significant retrieval improvements. [^128^] [^129^]

- **Hybrid search evolving from optional to required**: In 2024, hybrid search was a nice-to-have. By 2025, it's essential for code RAG. Weaviate, Qdrant, Milvus, and Turbopuffer all support native hybrid search. Redis, pgvector, and Elasticsearch also offer hybrid capabilities. [^33^] [^73^]

- **Reranking becoming a standard second-stage filter**: Cross-encoders (Cohere Rerank 4, ms-marco-MiniLM) improve RAG accuracy by 33-40% by scoring query-document pairs jointly. Production systems increasingly use two-stage retrieval: bi-encoder for fast candidate retrieval + cross-encoder for precise reranking. [^229^] [^234^]

- **Local-first and privacy-preserving indexing gaining traction**: Windsurf (local AST index), Continue.dev (LanceDB local), and Claude Code (no index, runtime search) represent a spectrum of privacy-first approaches. Cursor's path obfuscation (hashed paths, no source in cloud) addresses enterprise privacy concerns. [^74^] [^158^]

- **Graph databases (Neo4j) emerging as complementary to vector stores for code**: CodeRAG and Knowledge Graph Based Repository-Level Code Generation research show that dependency graphs (function calls, imports, inheritance) capture relationships vector similarity misses. Hybrid approaches combine vector retrieval for initial candidate selection + graph traversal for structural context. [^252^] [^261^] [^262^]

- **Incremental indexing via Merkle trees now standard**: Cursor, Claude Context, and Semantic Context MCP all use Merkle DAG hashing for change detection. Enables sub-second sync checks and avoids re-embedding unchanged files. [^63^] [^65^] [^67^]

---

### Controversies & Conflicting Claims

- **Long context vs. RAG debate**: With 1M-token context windows (Claude 3.5, Gemini), some argue RAG is unnecessary for codebases under ~1M tokens. However, long-context approaches cost $0.60/query vs. $0.012 for RAG (top-5 chunks), and RAG provides better precision by filtering noise. For 200K-token codebases, RAG remains 50x cheaper per query. [^125^]

- **Vector database vs. embedded/local storage**: Roo Code requires Qdrant (separate service), while Continue.dev uses LanceDB (embedded). A Roo Code GitHub issue (#6223) explicitly requests embedded alternatives to reduce setup time from 30-60 minutes to 5 minutes and eliminate 500MB+ RAM overhead. [^124^] [^158^]

- **Claude Code's "no index" philosophy vs. indexed competitors**: Claude Code uses pure agentic search (glob → grep → read → explore) with no persistent index, claiming always-fresh results with zero setup. Competitors (Cursor, Copilot) argue that semantic indexing enables discovery of conceptually related code regardless of naming. Benchmarks suggest indexed approaches retrieve ~90% relevant context vs. ~30% for keyword search on complex cross-cutting tasks. [^74^] [^166^]

- **Open-source vs. commercial embedding models for code**: Benchmarks show voyage-code-3 achieves 97.3% MRR vs. 95% for OpenAI text-embedding-3-small, but at 3x the cost ($0.06 vs. $0.02 per 1M tokens). The debate centers on whether the marginal quality improvement is worth the cost for production use. [^127^]

- **GraphRAG computational overhead**: Knowledge Graph Based Repository-Level Code Generation notes that "sub-graph retrieval and filtering is computationally intensive, especially for large repositories." The trade-off between richer context and retrieval latency remains unresolved. [^261^]

---

### Recommended Deep-Dive Areas

| Area | Why It Warrants Depth |
|------|----------------------|
| **Hybrid search fusion strategies** | RRF (Reciprocal Rank Fusion) vs. weighted sum vs. learned fusion. Milvus supports both `RRFRanker` and `WeightedRanker`. The optimal alpha (vector weight) varies by codebase and query type. [^209^] |
| **GraphRAG for code dependency analysis** | Structure-Grounded Knowledge Retrieval (SGKR) and CodeRAG demonstrate that function-call dependency graphs improve multi-step reasoning. Combining vector similarity with graph traversal (BFS on dependency paths) is an active research frontier. [^206^] [^252^] |
| **Embedding model quantization for large codebases** | voyage-code-3 supports binary embeddings at 256D that use 1/384th the storage of full float embeddings while still outperforming OpenAI v3-large by 4.81%. Matryoshka dimension reduction (1024→256D) enables significant cost savings. [^128^] |
| **MCP server patterns for code indexing services** | The MCP ecosystem is rapidly evolving. Understanding how to expose `codebase_search`, `full_index`, and `query` tools via MCP, with proper schema definitions and error handling, is critical for agent interoperability. [^207^] [^255^] |
| **Privacy-preserving indexing architectures** | Cursor's path obfuscation (hashed components + secret key + fixed nonce), zero-source-code-in-cloud design, and Merkle-tree-only-delta-sync represent the state of the art in secure codebase indexing. [^64^] [^69^] |

---

### Detailed Notes

#### 1. Vector Databases for Code Indexing

**Qdrant** (Roo Code's choice):
- Written in Rust, optimized for SIMD, best single-node latency (30-40ms P95 at 1M vectors) [^34^]
- Advanced payload filtering applied during (not after) vector search
- Free tier available (1GB forever); Qdrant Cloud from $25/mo
- 8,000-15,000 QPS at 1M vectors [^31^]
- Limitation: Manual shard management for replication in open-source version [^35^]

**Milvus / Zilliz Cloud** (Claude Context's choice):
- Disaggregated storage-compute microservices (query nodes, data nodes, index nodes) [^34^]
- Richest index selection: HNSW, IVF_FLAT, IVF_PQ, IVF_SQ8, DiskANN [^34^]
- GPU acceleration, designed for billions of vectors
- Milvus 2.6 adds: tiered storage (50% cost reduction), RaBitQ 1-bit quantization (72% memory reduction), faster BM25 (4x vs. Elasticsearch), up to 100K collections per cluster [^258^]
- Built-in hybrid search with `SPARSE_FLOAT_VECTOR` for BM25 + `FLOAT_VECTOR` for dense [^209^]

**Turbopuffer** (Cursor's choice):
- Serverless vector + full-text search backed by S3
- Unlimited namespaces (one per codebase) — active namespaces cached in NVMe, inactive fade to S3 [^68^]
- 20x cost reduction vs. traditional vector DB for Cursor's use case
- Warm query: 8-10ms; Cold query: 500-600ms [^61^]
- Copy-from-namespace for recycling vectors between namespaces [^68^]

**pgvector** (PostgreSQL extension):
- Best when already running PostgreSQL and <50M vectors [^33^]
- pgvectorscale achieves 471 QPS at 99% recall on 50M vectors (11.4x better than Qdrant at same recall, per May 2025 benchmarks) [^33^]
- Limitation: WHERE clauses applied after ANN search (full scan cost before filtering) [^30^]

**LanceDB** (Continue.dev's choice):
- Embedded TypeScript vector DB, runs in-process, stores on disk [^158^]
- Zero-copy, columnar storage. Handles 1M+ vectors with <10ms latency on modest hardware [^158^]
- SQL-like filtering. Ideal for local-first, privacy-preserving architectures [^158^]

**Decision Framework**:
| Situation | Recommendation |
|-----------|---------------|
| Existing PostgreSQL, <50M vectors | pgvector + pgvectorscale |
| Zero ops overhead, any scale | Pinecone |
| Open-source, self-hosted, performance-critical | Qdrant |
| Billions of vectors, enterprise, GPU | Milvus/Zilliz Cloud |
| Local-first, privacy-preserving, no separate process | LanceDB |
| Cost-optimized at massive scale (trillions) | Turbopuffer |
| Prototyping | Chroma |

#### 2. Embedding Models for Code

**voyage-code-3** (top choice for code):
- Outperforms OpenAI v3-large by 13.80% and CodeSage-large by 16.81% on 32 code retrieval datasets [^128^]
- 32K context length (vs. 8K OpenAI, 1K CodeSage) [^128^]
- 300+ programming languages [^126^]
- Matryoshka dimensions: 2048, 1024, 512, 256 [^128^]
- Quantization: float, int8, uint8, binary, ubinary [^128^]
- Binary at 256D: 1/384th storage of float3072, still outperforms OpenAI v3-large by 4.81% [^128^]
- Cost: $0.06 per 1M tokens [^127^]

**OpenAI text-embedding-3-small** (cost-effective default):
- 1536 dimensions, 8K context [^231^]
- Cost: $0.02 per 1M tokens (Batch tier: $0.01) [^231^]
- Benchmark: 95% MRR on CodeSearchNet (vs. 97.3% for voyage-code-3) [^127^]
- Best cost-performance ratio for general code retrieval [^231^]

**jina-code-embeddings** (new open-source contender):
- 0.5B and 1.5B parameter models [^129^]
- Autoregressive backbone pre-trained on text + code [^129^]
- Task-specific instruction prefixes + last-token pooling [^129^]
- Outperforms Qwen3-Embedding-0.6B and gemini-embedding-001 on MTEB-CoIR [^129^]

**CodeBERT / GraphCodeBERT** (legacy open-source):
- CodeBERT: 768D, trained on Python + Java, MRR only 11.7% on CodeSearchNet [^127^]
- GraphCodeBERT: adds AST structure understanding, MRR 50.9% [^127^]
- Significantly behind commercial models but free and self-hostable [^127^]

**Practical recommendation**: Use voyage-code-3 for mission-critical code search where accuracy matters most. Use OpenAI text-embedding-3-small for cost-sensitive applications or prototyping. Consider jina-code-embeddings for self-hosted scenarios.

#### 3. AST-Based Chunking vs. Text Splitting

**The cAST approach** (academic foundation):
- Parses source code into AST via tree-sitter, then applies recursive split-then-merge [^28^]
- Four design goals: syntactic integrity, high information density, language invariance, plug-and-play compatibility [^28^]
- Key advantage: "concatenating the chunks must reproduce the original file verbatim" [^28^]
- Paper shows naive chunking breaks return value context, causing incorrect code generation [^28^]

**code-chunk library** (production implementation):
- Extracts semantic entities: functions, methods, classes, interfaces, types, enums, imports [^151^] [^159^]
- Each entity gets: full signature, docstring, byte/line ranges, parent relationships [^151^]
- Builds scope tree: `UserService > getUser` context for embeddings [^159^]
- Supports overlap (last N lines from previous chunk), streaming, batch processing [^151^]
- Pre-formats `contextualizedText` optimized for embedding models [^151^]

**Production tool implementations**:
- Cursor: tree-sitter splits at function/class boundaries, ~500 token blocks [^61^] [^71^]
- Claude Context: AST-based primary, RecursiveCharacterTextSplitter fallback (1000 chunk, 200 overlap) [^63^]
- Windsurf Cascade: AST-level semantic blocks at function, method, class boundaries [^74^]
- Roo Code: tree-sitter parses semantic blocks (functions, classes, methods) [^21^]

**AST chunking advantage over text splitting**:
| Aspect | Text Splitting | AST Chunking |
|--------|---------------|--------------|
| Function boundaries | Often splits mid-function | Respects complete functions |
| Context preservation | Loses parent/scope info | Includes scope chain, imports |
| Language awareness | None | Language-agnostic via tree-sitter |
| Embedding quality | Lower (incomplete context) | Higher (complete syntactic units) |
| Retrieval accuracy | Baseline | Significantly better (per cAST paper) |

#### 4. GraphRAG for Code

**Structure-Grounded Knowledge Retrieval (SGKR)**:
- Represents domain knowledge as code dependency graph (DAG) via function-call relations [^206^]
- Each node: function code + associated domain knowledge [^206^]
- Edges: directed from caller to callee, representing functional dependency [^206^]
- Retrieval: extract semantic I/O tags from query → map to graph nodes → BFS for dependency paths connecting input to output nodes [^206^]
- Outperforms Dense-RAG and CodeBERT-RAG on multi-step data analysis benchmarks [^206^]

**CodeRAG with Dependency Graph**:
- Uses tree-sitter for chunking + Neo4j for dependency graph storage [^252^]
- Resolves intra-file function/class calls and inter-file imports [^252^]
- Retrieval: vector similarity top-K + graph neighbors (function calls, class calls, imports) [^252^]
- Query enhancement: LLM expands terse queries ("define loginController" → detailed description) [^252^]
- Live demo at code-rag.vercel.app [^252^]

**Knowledge Graph Based Repository-Level Code Generation**:
- Three-step pipeline: (1) Knowledge graph creation from code, (2) Hybrid graph-based retrieval, (3) LLM code generation with retrieved subgraph [^261^]
- Hybrid retrieval combines vector similarity on code documentation + graph traversal for dependencies [^261^]
- Achieves 36.36% pass@1 on EvoCodeBench with Claude-3.5 Sonnet, outperforming SOTA baselines [^261^]
- Key insight: "relational and usage-based contextual representation of code helps generate less error-prone and more functionally correct code" [^261^]

**Graph databases for code**:
- Neo4j stores both vector embeddings (cosine similarity search) and relationship edges [^252^] [^262^]
- Supports combined structured queries (Cypher for relationships) + vector search (for semantic similarity) [^262^]
- LangChain agents can use separate tools for vector QA (documentation search) and graph queries (dependency navigation) [^262^]

#### 5. Incremental Indexing with Git/Merkle Trees

**Merkle tree mechanism**:
- Each file → content hash → directory hash (from children) → root hash [^63^] [^67^]
- Sync check compares current root with last snapshot; if match → skip (milliseconds) [^67^]
- If root differs → walk tree to find changed files (added, deleted, modified) [^63^]
- Only recompute embeddings for changed files [^63^]

**Production implementations**:
- **Cursor**: Client computes Merkle tree, syncs with server every ~5-10 min. Delta diffs sent to AWS-cached embedding storage. Indexing time dropped from 7.87s median to 525ms after optimization. [^61^] [^71^]
- **Claude Context**: Local sync state stored under `~/.context/merkle/`. 3-stage sync: quick check → precise diff → incremental update. [^63^] [^67^]
- **Semantic Context MCP**: Merkle Tree built from file content hashes. Periodic incremental updates every 5 minutes. [^65^]

**Benefits**:
- No-change check completes in milliseconds
- Only changed files re-embedded (saves embedding API costs)
- Vector index stays fresh without full rebuild
- After restart, restores from serialized Merkle tree

#### 6. Token/Cost Considerations for Large Codebases

**Embedding costs for initial indexing**:
| Codebase Size | Tokens | OpenAI 3-small | Voyage code-3 |
|--------------|--------|----------------|---------------|
| Small (20-30K lines) | ~50K tokens | ~$0.05 | ~$0.15 |
| Medium (200K lines) | ~500K tokens | ~$0.50 | ~$1.50 |
| Large (1.5M lines) | ~5M tokens | ~$5.00 | ~$15.00 |
| VS Code (1.5M lines, Claude Context) | ~5M tokens | ~$1.06 actual | N/A |

Note: Claude Context's $1.06 for VS Code codebase suggests they use efficient chunking that reduces total tokens. [^208^]

**Per-query cost comparison** (200K-token knowledge base):
| Approach | Tokens sent to LLM | Cost per query |
|----------|-------------------|----------------|
| Traditional RAG (top-5 chunks) | ~4,000 tokens | ~$0.012 |
| Long context (full corpus) | ~200,000 tokens | ~$0.60 |
| Long context + prompt caching | ~200,000 tokens | ~$0.06 |

RAG is 50x cheaper per query than long-context approaches. [^125^]

**Vector database hosting costs**:
| Scale | pgvector | Pinecone | Qdrant Cloud | Milvus (Zilliz) |
|-------|----------|----------|--------------|-----------------|
| 100K vectors | $0 | $0 (free) | $0 (free) | $0 (free) |
| 1M vectors | $0-50 | $70-231 | $25-75 | $99+ |
| 10M vectors | $200-400 | $500-1,500 | $200-500 | $300-600 |
| 100M vectors | Not rec. | $2,000-5,000 | $1,000-3,000 | $1,500-4,000 |

**Total system cost considerations**:
- Managed vector DB: $50-300/month typical
- Embedding API costs: initial + incremental updates
- Engineering time: building, maintaining retrieval pipeline
- Retrieval failure rate: hard to quantify but costly in production [^125^]

**Claude Context benchmark results**:
- 39.4% token reduction at equivalent retrieval quality [^63^]
- 36.1% reduction in tool calls [^63^]
- 62% fewer tokens on Xarray swap_dims benchmark [^63^]
- Xarray benchmark: 40 seconds, 23K tokens (Claude Context) vs. 12 seconds, 18K tokens (grep) for function search — but Claude Context returns correct file, grep doesn't [^208^]

#### 7. Hybrid Search (BM25 + Vector)

**Why hybrid search matters for code**:
- Dense vectors catch semantic similarity ("authentication helper" matches `verifySignature`)
- BM25 catches exact identifiers (function names, error codes, file paths)
- Combined via Reciprocal Rank Fusion (RRF) or weighted sum [^59^] [^70^]
- RRF formula: score = sum of 1/(rank + k) across both result sets (k=60 typically) [^60^]

**Implementations**:
- **Claude Context**: Milvus `SPARSE_FLOAT_VECTOR` for BM25 + `FLOAT_VECTOR` for dense, merged via RRF. Reports ~40% token reduction vs. pure grep. [^63^] [^209^]
- **Cursor**: Turbopuffer's native vector + full-text hybrid. Obfuscated paths enable path-based filtering. [^64^]
- **Roo Code**: Qdrant with cosine similarity. Community requests hybrid search addition. [^21^]
- **GitHub Copilot**: Remote semantic search + local TF-IDF fallback + embeddings for <750 files. [^153^]

**Fusion strategies**:
- **RRF (Reciprocal Rank Fusion)**: Simple, no hyperparameters, robust. `score = sum(1/(k + rank))` [^209^]
- **Weighted sum**: `alpha * vector_score + (1-alpha) * bm25_score`. Alpha=0.5 is safe default. [^59^]
- **Milvus**: Supports `RRFRanker` and `WeightedRanker` natively [^209^]

**Re-ranking as a third stage**:
- Two-stage: bi-encoder retrieval → cross-encoder reranking
- Cross-encoders (Cohere Rerank 4, ms-marco-MiniLM) process query+document jointly via full self-attention [^229^]
- MIT study: +33% accuracy average across 8 benchmarks [^234^]
- Cursor uses fine-tuned 7B CodeLlama reranker, processing up to 500K tokens/query via blob-storage KV caching [^66^]
- Recommended pipeline: bi-encoder top-50 → cross-encoder rerank top-10 → LLM [^229^]

#### 8. Metadata to Store with Code Embeddings

**Critical metadata fields**:
| Field | Purpose | Source |
|-------|---------|--------|
| `file_path` | Navigation, path filtering, result display | File system |
| `line_start` / `line_end` | Precise location for displaying results | AST parser (tree-sitter) |
| `node_type` | function_declaration, class_definition, etc. | AST |
| `node_name` | Function name, class name for exact matching | AST |
| `signature` | Full signature: `async getUser(id: string): Promise<User>` | AST + type info |
| `parent` | Containing class/function (scope chain) | AST scope tree |
| `imports` | Module dependencies for this chunk | AST import extraction |
| `docstring` | Documentation comments for semantic context | AST comment extraction |
| `language` | Programming language for filtering | File extension or AST |
| `content_hash` | For Merkle tree change detection | SHA-256 of content |
| `last_modified` | Timestamp for freshness filtering | File system |

**Rich context extraction (code-chunk approach)**:
- Scope chain: `UserService > getUser` format [^159^]
- Siblings: what comes before/after for continuity [^159^]
- Entity signatures with types for better embedding understanding [^151^]
- Import resolution: which modules this chunk depends on [^252^]
- Call relationships: functions called by this chunk [^252^]

**Privacy-preserving metadata (Cursor approach)**:
- File paths obfuscated: each component hashed with secret key + fixed nonce [^64^]
- Only metadata in cloud: hashed paths, line ranges, embedding vectors
- Raw source code never leaves machine; client reads local files by obfuscated ID [^69^]

#### 9. Building a Code Indexing Service for Expert Agents

**MCP Server Architecture**:
- MCP server exposes tools (functions) with JSON Schema descriptions [^255^]
- Agent discovers tools via `tools/list`, invokes via `tools/call` [^130^]
- Transport: stdio (local) or HTTP with SSE (remote) [^130^]

**Key tools for a code indexing MCP server**:
| Tool | Description |
|------|-------------|
| `full_index` | Index entire codebase (triggered on first use) |
| `query` | Hybrid search with natural language query |
| `status` | Check indexing status (indexed/indexing/error) |
| `clear_index` | Remove all indexed data |
| `incremental_sync` | Trigger manual incremental update |
| `search_code` | Find symbol definitions (like ctags) |
| `read_file_content` | Read specific file for full context |

**Production MCP server examples**:
- **Claude Context** (`@zilliz/claude-context-mcp`): Indexes into Milvus/Zilliz Cloud. 4 tools: index, search, clear, status. Supports 9+ languages. [^5^]
- **code-index-mcp**: Uses ctags for symbol indexing. Tools: `search_code`, `read_file_content`. No embeddings — pure symbol navigation. [^254^]
- **semantic-context-mcp**: Uses ChromaDB locally. Merkle tree incremental indexing. Tools: `full_index`, `status`, `query`. [^65^]

**Implementation checklist for a code indexing service**:
1. **Chunking layer**: tree-sitter AST parser → extract functions/classes/methods with metadata
2. **Embedding layer**: configurable provider (OpenAI, Voyage, Ollama, Gemini)
3. **Storage layer**: vector database (Qdrant, Milvus, or LanceDB) with metadata fields
4. **Indexing layer**: Merkle tree for change detection + incremental sync
5. **Search layer**: hybrid BM25 + dense vector with RRF fusion
6. **MCP layer**: expose tools with clear descriptions and schemas
7. **Privacy layer**: path obfuscation, local-first option, configurable data residency

**Key design decisions**:
- Choose embedded DB (LanceDB) for local-first, separate service (Qdrant/Milvus) for team sharing
- Support multiple embedding providers with adapter pattern (as Roo Code plans) [^124^]
- Store both the raw chunk and `contextualizedText` (with scope/imports) for embeddings [^151^]
- Implement batched embedding for initial indexing (parallel API calls)
- Use debounced file watchers for real-time incremental updates
- Consider adding a reranker stage for high-value queries (cross-encoder or LLM-based)
