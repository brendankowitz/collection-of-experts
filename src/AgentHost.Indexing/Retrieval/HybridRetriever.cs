using AgentHost.Indexing.Embeddings;
using AgentHost.Indexing.Storage;
using Microsoft.Extensions.Logging;

namespace AgentHost.Indexing.Retrieval;

/// <summary>
/// Two-stage retriever: vector search via Qdrant, then BM25-style keyword
/// score blending over chunk metadata for re-ranking before returning the final top-K.
/// </summary>
public sealed class HybridRetriever : IRetriever
{
    private readonly IVectorStore _vectors;
    private readonly IEmbedder _embedder;
    private readonly IReranker _reranker;
    private readonly ILogger<HybridRetriever> _logger;

    private const string CodeCollection = "code-chunks";
    private const int VectorCandidates = 100;
    private const float VectorWeight = 0.7f;
    private const float KeywordWeight = 0.3f;

    /// <summary>Creates the hybrid retriever.</summary>
    public HybridRetriever(
        IVectorStore vectors,
        IEmbedder embedder,
        IReranker reranker,
        ILogger<HybridRetriever> logger)
    {
        _vectors = vectors;
        _embedder = embedder;
        _reranker = reranker;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalHit>> SearchAsync(
        string repoId,
        string query,
        int k,
        CancellationToken ct = default)
    {
        // 1. Embed the query
        var queryVectors = await _embedder.EmbedAsync([query], ct);
        var queryVector = queryVectors[0];

        // 2. Vector search top-candidates filtered by repo
        var filter = new Dictionary<string, string> { ["repo_id"] = repoId };
        var vectorHits = await _vectors.SearchAsync(
            CodeCollection, queryVector, VectorCandidates, filter, ct);

        if (vectorHits.Count == 0)
            return [];

        // 3. Build keyword tokens from query
        var queryTokens = Tokenise(query);

        // 4. Score-blend using payload data (no extra DB round-trip)
        var scored = new List<(RetrievalHit Hit, float Score)>();
        foreach (var vhit in vectorHits)
        {
            var p = vhit.Payload;
            string filePath   = p.TryGetValue("file_path",   out var fp) ? fp : string.Empty;
            string language   = p.TryGetValue("language",    out var lang) ? lang : string.Empty;
            string symbolName = p.TryGetValue("symbol_name", out var sn) ? sn : string.Empty;
            string content    = p.TryGetValue("content",     out var ct2) ? ct2 : string.Empty;
            int startLine     = p.TryGetValue("start_line",  out var sl) && int.TryParse(sl, out var s) ? s : 0;
            int endLine       = p.TryGetValue("end_line",    out var el) && int.TryParse(el, out var e) ? e : 0;

            float keywordScore = queryTokens.Count > 0
                ? ComputeKeywordScore(queryTokens, filePath, symbolName)
                : 0f;

            float blended = VectorWeight * vhit.Score + KeywordWeight * keywordScore;

            scored.Add((new RetrievalHit(
                filePath, content, blended, startLine, endLine,
                language, string.IsNullOrEmpty(symbolName) ? null : symbolName), blended));
        }

        var topK = scored
            .OrderByDescending(s => s.Score)
            .Take(k)
            .Select(s => s.Hit)
            .ToList();

        // 5. Optional reranking
        var reranked = await _reranker.RerankAsync(query, topK, ct);
        _logger.LogDebug("HybridRetriever returned {Count} hits for repo {RepoId}", reranked.Count, repoId);
        return reranked;
    }

    private static List<string> Tokenise(string text)
        => text.ToLowerInvariant()
            .Split([' ', '\t', '\n', '.', '/', '-', '_', '(', ')', '<', '>', ','],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .ToList();

    private static float ComputeKeywordScore(List<string> tokens, string filePath, string symbolName)
    {
        var haystack = $"{filePath} {symbolName}".ToLowerInvariant();
        int matched = tokens.Count(t => haystack.Contains(t, StringComparison.Ordinal));
        return tokens.Count > 0 ? (float)matched / tokens.Count : 0f;
    }
}
