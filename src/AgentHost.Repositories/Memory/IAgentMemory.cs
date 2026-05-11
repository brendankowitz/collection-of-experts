namespace AgentHost.Repositories.Memory;

/// <summary>
/// Three-layer agent memory interface:
/// <list type="bullet">
///   <item>Short-term: raw turn log per thread.</item>
///   <item>Long-term recall: semantic search over persisted summaries.</item>
///   <item>Summarise &amp; persist: distil a thread into a long-term memory entry.</item>
/// </list>
/// </summary>
public interface IAgentMemory
{
    /// <summary>Records a single conversation turn in the short-term log.</summary>
    Task RecordTurnAsync(string agentId, string threadId, string role, string content, CancellationToken ct = default);

    /// <summary>
    /// Recalls up to <paramref name="k"/> relevant long-term memory entries for the given query.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> RecallAsync(string agentId, string threadId, string query, int k = 5, CancellationToken ct = default);

    /// <summary>
    /// Summarises the current short-term thread via LLM, embeds the summary, and persists it
    /// as a long-term memory entry.  Safe to call fire-and-forget.
    /// </summary>
    Task SummarizeAndPersistAsync(string agentId, string threadId, CancellationToken ct = default);

    /// <summary>
    /// Deletes ALL memory (short-term turns and long-term entries) for the given thread.
    /// Used by privacy / admin flows.
    /// </summary>
    Task PurgeThreadAsync(string threadId, CancellationToken ct = default);
}
