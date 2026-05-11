using AgentHost.Indexing.Options;
using AgentHost.Indexing.Retrieval;
using Microsoft.Extensions.Options;

namespace AgentHost.Services;

/// <summary>
/// Drop-in compatibility wrapper around <see cref="MockCodeIndexService"/> that
/// optionally delegates to the real <see cref="IRetriever"/> when
/// <c>Indexing:UseRealRetriever=true</c> is set.
/// </summary>
/// <remarks>
/// Agents continue to depend on <see cref="MockCodeIndexService"/> unchanged.
/// Phase 3 can flip the feature flag to enable live retrieval without modifying agent code.
/// </remarks>
public sealed class MockCodeIndexServiceCompat : MockCodeIndexService
{
    private readonly IRetriever? _retriever;
    private readonly bool _useReal;

    /// <summary>Creates the compat shim.</summary>
    /// <param name="options">Indexing options (reads <c>UseRealRetriever</c>).</param>
    /// <param name="retriever">Real retriever, or <c>null</c> when not registered.</param>
    public MockCodeIndexServiceCompat(
        IOptions<IndexingOptions> options,
        IRetriever? retriever = null)
    {
        _useReal = options.Value.UseRealRetriever;
        _retriever = retriever;
    }

    /// <summary>
    /// Searches the index.  Delegates to <see cref="IRetriever"/> when the feature
    /// flag is on; otherwise falls back to the mock keyword search.
    /// </summary>
    public new List<(string FilePath, string Snippet)> Search(string repo, string query)
    {
        if (_useReal && _retriever is not null)
        {
            var hits = _retriever
                .SearchAsync(repo, query, k: 10)
                .GetAwaiter().GetResult();
            return hits.Select(h => (h.FilePath, h.Snippet)).ToList();
        }

        return base.Search(repo, query);
    }
}
