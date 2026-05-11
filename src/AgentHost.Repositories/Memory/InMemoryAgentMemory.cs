using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AgentHost.Repositories.Memory;

/// <summary>
/// In-memory <see cref="IAgentMemory"/> for tests and the default development setup.
/// <para>
/// Short-term: turns stored per (agentId, threadId) in a ConcurrentDictionary.
/// Long-term recall: returns entries ordered by recency (most recent first), filtered by agentId.
/// SummarizeAndPersistAsync creates a naive summary (concatenated turns) — no LLM or Qdrant dependency.
/// </para>
/// </summary>
public sealed class InMemoryAgentMemory : IAgentMemory
{
    // (agentId, threadId) → ordered list of turns
    private readonly ConcurrentDictionary<(string, string), List<TurnRecord>> _turns = new();

    // agent_memory rows keyed by Id
    private readonly ConcurrentDictionary<Guid, MemoryEntry> _entries = new();

    private readonly ILogger<InMemoryAgentMemory> _logger;

    public InMemoryAgentMemory(ILogger<InMemoryAgentMemory> logger) => _logger = logger;

    public Task RecordTurnAsync(string agentId, string threadId, string role, string content, CancellationToken ct = default)
    {
        var key = (agentId, threadId);
        var list = _turns.GetOrAdd(key, _ => []);
        lock (list) list.Add(new TurnRecord(agentId, threadId, role, content, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns up to <paramref name="k"/> long-term memory entries ordered by recency
    /// (most recently created first) for the given agent.
    /// InMemoryAgentMemory does NOT perform semantic search — recall is by recency only.
    /// </summary>
    public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string agentId, string threadId, string query, int k = 5, CancellationToken ct = default)
    {
        var results = _entries.Values
            .Where(e => e.AgentId == agentId && (e.ExpiresAt is null || e.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(e => e.CreatedAt)
            .Take(k)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryEntry>>(results);
    }

    public Task SummarizeAndPersistAsync(string agentId, string threadId, CancellationToken ct = default)
    {
        var key = (agentId, threadId);
        if (!_turns.TryGetValue(key, out var turns)) return Task.CompletedTask;

        List<TurnRecord> snapshot;
        lock (turns) snapshot = [.. turns];

        if (snapshot.Count == 0) return Task.CompletedTask;

        var summary = string.Join(" | ", snapshot.Select(t => $"[{t.Role}] {t.Content}"));
        var entry = new MemoryEntry(
            Id: Guid.NewGuid(),
            AgentId: agentId,
            ThreadId: threadId,
            Summary: summary,
            Tags: [],
            VectorId: null,
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddDays(30));

        _entries[entry.Id] = entry;
        _logger.LogInformation("Persisted memory entry {Id} for agent {AgentId} / thread {ThreadId}", entry.Id, agentId, threadId);
        return Task.CompletedTask;
    }

    public Task PurgeThreadAsync(string threadId, CancellationToken ct = default)
    {
        // Remove short-term turns for this thread across all agents
        foreach (var key in _turns.Keys.Where(k => k.Item2 == threadId).ToList())
            _turns.TryRemove(key, out _);

        // Remove long-term entries for this thread
        foreach (var entry in _entries.Values.Where(e => e.ThreadId == threadId).ToList())
            _entries.TryRemove(entry.Id, out _);

        _logger.LogInformation("Purged all memory for thread {ThreadId}", threadId);
        return Task.CompletedTask;
    }

    // Exposed for testing only
    internal IReadOnlyList<MemoryEntry> AllEntries => [.. _entries.Values];
    internal IReadOnlyList<TurnRecord> AllTurns => _turns.Values.SelectMany(l => { lock (l) return l.ToList(); }).ToList();
}
