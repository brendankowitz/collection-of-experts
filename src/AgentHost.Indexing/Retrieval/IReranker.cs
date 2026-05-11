namespace AgentHost.Indexing.Retrieval;

/// <summary>
/// Seam for re-ranking retrieval results.  The default implementation is a no-op.
/// </summary>
public interface IReranker
{
    /// <summary>Optionally re-orders and/or filters <paramref name="hits"/>.</summary>
    Task<IReadOnlyList<RetrievalHit>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalHit> hits,
        CancellationToken ct = default);
}

/// <summary>Pass-through reranker that returns results unchanged.</summary>
public sealed class NoopReranker : IReranker
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RetrievalHit>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalHit> hits,
        CancellationToken ct = default)
        => Task.FromResult(hits);
}
