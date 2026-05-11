namespace AgentHost.Indexing.Indexer;

/// <summary>
/// Fetches repository source files for indexing, supporting both full clones
/// and incremental (diff-only) fetches.
/// </summary>
public interface IRepositoryFetcher
{
    /// <summary>
    /// Ensures the repository at <paramref name="url"/> is available locally and
    /// returns all source files that need indexing.
    /// </summary>
    /// <param name="repoId">Logical repository identifier.</param>
    /// <param name="url">Clone URL.</param>
    /// <param name="branch">Branch to check out (default branch if <c>null</c>).</param>
    /// <param name="sinceCommit">If set, return only files changed since this commit SHA (incremental mode).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A dictionary from relative file path to file content.
    /// </returns>
    Task<FetchResult> FetchAsync(
        string repoId,
        string url,
        string? branch = null,
        string? sinceCommit = null,
        CancellationToken ct = default);
}

/// <summary>Result of a repository fetch operation.</summary>
/// <param name="HeadCommit">Current HEAD commit SHA.</param>
/// <param name="Files">Files to index: relative path → content.</param>
public sealed record FetchResult(
    string HeadCommit,
    IReadOnlyDictionary<string, string> Files);
