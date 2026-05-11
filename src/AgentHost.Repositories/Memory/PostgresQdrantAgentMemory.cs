using System.Text.Json;
using AgentHost.Indexing.Embeddings;
using AgentHost.Indexing.Storage;
using AgentHost.Llm;
using AgentHost.Repositories.Options;
using Dapper;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentHost.Repositories.Memory;

/// <summary>
/// Production <see cref="IAgentMemory"/> backed by Postgres (short-term turns + long-term summaries)
/// and Qdrant (vector recall).
/// Call <see cref="MigrateAsync"/> once at startup.
/// </summary>
public sealed class PostgresQdrantAgentMemory : IAgentMemory
{
    private readonly string _connectionString;
    private readonly string _collection;
    private readonly int _defaultRetentionDays;
    private readonly IVectorStore _vectorStore;
    private readonly IEmbedder _embedder;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<PostgresQdrantAgentMemory> _logger;

    public PostgresQdrantAgentMemory(
        IOptions<PersistenceOptions> options,
        IVectorStore vectorStore,
        IEmbedder embedder,
        IChatClientFactory chatClientFactory,
        ILogger<PostgresQdrantAgentMemory> logger)
    {
        var mem = options.Value.Memory;
        _connectionString = mem.Postgres.ConnectionString
            ?? throw new InvalidOperationException("Persistence:Memory:Postgres:ConnectionString is required when backend is Postgres+Qdrant");
        _collection = mem.Qdrant.Collection;
        _defaultRetentionDays = mem.DefaultRetentionDays;
        _vectorStore = vectorStore;
        _embedder = embedder;
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    private NpgsqlConnection Open() => new(_connectionString);

    // ── Migration ─────────────────────────────────────────────────────────────

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS agent_memory_turns (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                agent_id    TEXT NOT NULL,
                thread_id   TEXT NOT NULL,
                role        TEXT NOT NULL,
                content     TEXT NOT NULL,
                recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_memory_turns_thread ON agent_memory_turns(thread_id);

            CREATE TABLE IF NOT EXISTS agent_memory (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                agent_id    TEXT NOT NULL,
                thread_id   TEXT NOT NULL,
                summary     TEXT NOT NULL,
                tags        JSONB NOT NULL DEFAULT '[]',
                vector_id   UUID,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at  TIMESTAMPTZ
            );

            CREATE INDEX IF NOT EXISTS idx_agent_memory_agent    ON agent_memory(agent_id);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_thread   ON agent_memory(thread_id);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_expires  ON agent_memory(expires_at);
            """;

        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql);

        await _vectorStore.EnsureCollectionAsync(_collection, (uint)_embedder.Dimensions, ct);
        _logger.LogInformation("agent_memory schema migration completed");
    }

    // ── IAgentMemory ──────────────────────────────────────────────────────────

    public async Task RecordTurnAsync(string agentId, string threadId, string role, string content, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO agent_memory_turns (agent_id, thread_id, role, content)
            VALUES (@AgentId, @ThreadId, @Role, @Content)
            """,
            new { AgentId = agentId, ThreadId = threadId, Role = role, Content = content });
    }

    public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(string agentId, string threadId, string query, int k = 5, CancellationToken ct = default)
    {
        // Embed the query, search Qdrant, then hydrate from Postgres
        var vectors = await _embedder.EmbedAsync([query], ct);
        var hits = await _vectorStore.SearchAsync(
            _collection, vectors[0], k,
            filter: new Dictionary<string, string> { ["agent_id"] = agentId },
            ct: ct);

        if (hits.Count == 0) return [];

        var ids = hits.Select(h => h.Id).ToList();
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(
            "SELECT * FROM agent_memory WHERE id = ANY(@Ids) AND (expires_at IS NULL OR expires_at > NOW())",
            new { Ids = ids.ToArray() });

        return rows.Select(MapMemory).ToList();
    }

    public async Task SummarizeAndPersistAsync(string agentId, string threadId, CancellationToken ct = default)
    {
        // Load turns
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var turns = (await conn.QueryAsync<dynamic>(
            "SELECT role, content FROM agent_memory_turns WHERE agent_id = @AgentId AND thread_id = @ThreadId ORDER BY recorded_at",
            new { AgentId = agentId, ThreadId = threadId })).ToList();

        if (turns.Count == 0) return;

        var transcript = string.Join("\n", turns.Select(t => $"{t.role}: {t.content}"));

        // Summarise via LLM
        var client = _chatClientFactory.CreateForAgent(agentId);
        var summaryResponse = await client.GetResponseAsync(
            $"Summarise the following conversation in 2-3 sentences for future retrieval:\n\n{transcript}",
            cancellationToken: ct);
        var summary = summaryResponse.Text?.Trim() ?? transcript[..Math.Min(500, transcript.Length)];

        // Embed summary
        var vectors = await _embedder.EmbedAsync([summary], ct);
        var vectorId = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddDays(_defaultRetentionDays);

        // Persist to Postgres
        var entryId = Guid.NewGuid();
        await conn.ExecuteAsync("""
            INSERT INTO agent_memory (id, agent_id, thread_id, summary, tags, vector_id, expires_at)
            VALUES (@Id, @AgentId, @ThreadId, @Summary, @Tags::jsonb, @VectorId, @ExpiresAt)
            """,
            new
            {
                Id = entryId,
                AgentId = agentId,
                ThreadId = threadId,
                Summary = summary,
                Tags = "[]",
                VectorId = vectorId,
                ExpiresAt = expiresAt
            });

        // Upsert into Qdrant
        await _vectorStore.UpsertAsync(_collection,
        [
            new VectorPoint(vectorId, vectors[0], new Dictionary<string, string>
            {
                ["agent_id"]  = agentId,
                ["thread_id"] = threadId,
                ["entry_id"]  = entryId.ToString()
            })
        ], ct);

        _logger.LogInformation("Persisted memory entry {EntryId} for agent {AgentId} / thread {ThreadId}", entryId, agentId, threadId);
    }

    public async Task PurgeThreadAsync(string threadId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);

        // Get vector IDs before deleting rows
        var vectorIds = (await conn.QueryAsync<Guid?>(
            "SELECT vector_id FROM agent_memory WHERE thread_id = @ThreadId AND vector_id IS NOT NULL",
            new { ThreadId = threadId })).ToList();

        await conn.ExecuteAsync("DELETE FROM agent_memory_turns WHERE thread_id = @ThreadId", new { ThreadId = threadId });
        await conn.ExecuteAsync("DELETE FROM agent_memory WHERE thread_id = @ThreadId", new { ThreadId = threadId });

        // Remove vectors from Qdrant
        foreach (var vid in vectorIds.Where(v => v.HasValue))
            await _vectorStore.DeleteByFilterAsync(_collection, new Dictionary<string, string> { ["thread_id"] = threadId }, ct);

        _logger.LogInformation("Purged all memory for thread {ThreadId}", threadId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MemoryEntry MapMemory(dynamic r) => new(
        Id: (Guid)r.id,
        AgentId: (string)r.agent_id,
        ThreadId: (string)r.thread_id,
        Summary: (string)r.summary,
        Tags: JsonSerializer.Deserialize<List<string>>((string)r.tags) ?? [],
        VectorId: (Guid?)r.vector_id,
        CreatedAt: (DateTime)r.created_at,
        ExpiresAt: (DateTime?)r.expires_at);
}
