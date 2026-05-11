using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgentHost.Repositories.Tasks;

/// <summary>
/// Postgres-backed <see cref="IAgentTaskStore"/> using Npgsql + Dapper.
/// Tables: <c>agent_tasks</c> and <c>agent_task_messages</c>.
/// Call <see cref="MigrateAsync"/> once at startup before use.
/// </summary>
public sealed class PostgresAgentTaskStore : IAgentTaskStore
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresAgentTaskStore> _logger;

    public PostgresAgentTaskStore(string connectionString, ILogger<PostgresAgentTaskStore> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    private NpgsqlConnection Open() => new(_connectionString);

    // ── Migration ─────────────────────────────────────────────────────────────

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS agent_tasks (
                id              TEXT PRIMARY KEY,
                agent_id        TEXT NOT NULL,
                session_id      TEXT,
                state           TEXT NOT NULL DEFAULT 'Submitted',
                created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                error_message   TEXT,
                metadata        JSONB
            );

            CREATE INDEX IF NOT EXISTS idx_agent_tasks_agent ON agent_tasks(agent_id);

            CREATE TABLE IF NOT EXISTS agent_task_messages (
                id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                task_id     TEXT NOT NULL REFERENCES agent_tasks(id) ON DELETE CASCADE,
                role        TEXT NOT NULL,
                content     TEXT NOT NULL,
                created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_agent_task_messages_task ON agent_task_messages(task_id);
            """;

        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql);
        _logger.LogInformation("agent_tasks schema migration completed");
    }

    // ── IAgentTaskStore ───────────────────────────────────────────────────────

    public async Task<TaskRecord> CreateTaskAsync(string sessionId, string agentId, string role, string content, CancellationToken ct = default)
    {
        var task = new TaskRecord
        {
            SessionId = sessionId,
            AgentId = agentId,
            State = TaskState.Submitted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var conn = Open();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync("""
            INSERT INTO agent_tasks (id, agent_id, session_id, state, created_at, updated_at, last_message_at)
            VALUES (@Id, @AgentId, @SessionId, @State, @CreatedAt, @UpdatedAt, @UpdatedAt)
            """,
            new { task.Id, task.AgentId, task.SessionId, State = task.State.ToString(), task.CreatedAt, task.UpdatedAt });

        await InsertMessageAsync(conn, task.Id, role, content, ct);
        task.Messages.Add(new TaskMessageRecord(role, content, task.CreatedAt));

        _logger.LogInformation("Created task {TaskId} for agent {AgentId}", task.Id, agentId);
        return task;
    }

    public async Task<TaskRecord?> GetTaskAsync(string taskId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM agent_tasks WHERE id = @Id", new { Id = taskId });
        if (row is null) return null;

        var task = MapTask(row);

        var msgRows = await conn.QueryAsync<dynamic>(
            "SELECT role, content, created_at FROM agent_task_messages WHERE task_id = @TaskId ORDER BY created_at",
            new { TaskId = taskId });

        foreach (var m in msgRows)
            task.Messages.Add(new TaskMessageRecord((string)m.role, (string)m.content, (DateTime)m.created_at));

        return task;
    }

    public async Task<bool> UpdateTaskAsync(string taskId, TaskState state, string? errorMessage = null, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync("""
            UPDATE agent_tasks
            SET state = @State, updated_at = NOW(), error_message = @ErrorMessage
            WHERE id = @Id
            """,
            new { Id = taskId, State = state.ToString(), ErrorMessage = errorMessage });
        _logger.LogInformation("Task {TaskId} → {State}", taskId, state);
        return affected > 0;
    }

    public async Task<bool> AppendMessageAsync(string taskId, string role, string content, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM agent_tasks WHERE id = @Id)", new { Id = taskId });
        if (!exists) return false;
        await InsertMessageAsync(conn, taskId, role, content, ct);
        await conn.ExecuteAsync(
            "UPDATE agent_tasks SET last_message_at = NOW(), updated_at = NOW() WHERE id = @Id",
            new { Id = taskId });
        return true;
    }

    public async Task<bool> CompleteTaskAsync(string taskId, string role, string content, CancellationToken ct = default)
    {
        if (!await AppendMessageAsync(taskId, role, content, ct)) return false;
        return await UpdateTaskAsync(taskId, TaskState.Completed, null, ct);
    }

    public async Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync("""
            UPDATE agent_tasks
            SET state = 'Canceled', updated_at = NOW()
            WHERE id = @Id AND state NOT IN ('Completed','Failed','Canceled')
            """,
            new { Id = taskId });
        return affected > 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task InsertMessageAsync(NpgsqlConnection conn, string taskId, string role, string content, CancellationToken ct)
    {
        await conn.ExecuteAsync("""
            INSERT INTO agent_task_messages (task_id, role, content) VALUES (@TaskId, @Role, @Content)
            """,
            new { TaskId = taskId, Role = role, Content = content });
    }

    private static TaskRecord MapTask(dynamic r) => new()
    {
        Id = (string)r.id,
        AgentId = (string)r.agent_id,
        SessionId = (string?)r.session_id,
        State = Enum.Parse<TaskState>((string)r.state, ignoreCase: true),
        CreatedAt = (DateTime)r.created_at,
        UpdatedAt = (DateTime)r.updated_at,
        LastMessageAt = (DateTime)r.last_message_at,
        ErrorMessage = (string?)r.error_message,
    };
}
