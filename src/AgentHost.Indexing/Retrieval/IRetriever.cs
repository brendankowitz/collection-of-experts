namespace AgentHost.Indexing.Retrieval;

/// <summary>
/// Searches a repository's indexed code using hybrid vector + keyword retrieval.
/// </summary>
public interface IRetriever
{
    /// <summary>
    /// Searches the indexed code for <paramref name="query"/> within <paramref name="repoId"/>.
    /// </summary>
    /// <param name="repoId">Repository to search.</param>
    /// <param name="query">Natural-language or code query string.</param>
    /// <param name="k">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<RetrievalHit>> SearchAsync(
        string repoId,
        string query,
        int k,
        CancellationToken ct = default);
}

/// <summary>A single retrieval result.</summary>
/// <param name="FilePath">Relative file path within the repository.</param>
/// <param name="Snippet">The code snippet (chunk content).</param>
/// <param name="Score">Blended relevance score (higher is better).</param>
/// <param name="StartLine">1-based start line in the file.</param>
/// <param name="EndLine">1-based end line in the file.</param>
/// <param name="Language">Detected programming language.</param>
/// <param name="SymbolName">Symbol name if the chunk is a named declaration.</param>
public sealed record RetrievalHit(
    string FilePath,
    string Snippet,
    float Score,
    int StartLine,
    int EndLine,
    string Language,
    string? SymbolName);
