using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AgentHost.Repositories.Tasks;

/// <summary>
/// Thread-safe, in-memory <see cref="IAgentTaskStore"/>.
/// Tasks expire one hour after creation and are removed on next access.
/// This is the default backend and is used in tests.
/// </summary>
public sealed class InMemoryAgentTaskStore : IAgentTaskStore
{
    private readonly ConcurrentDictionary<string, TaskRecord> _tasks = new();
    private readonly ILogger<InMemoryAgentTaskStore> _logger;

    public InMemoryAgentTaskStore(ILogger<InMemoryAgentTaskStore> logger) => _logger = logger;

    public Task<TaskRecord> CreateTaskAsync(string sessionId, string agentId, string role, string content, CancellationToken ct = default)
    {
        var task = new TaskRecord
        {
            SessionId = sessionId,
            AgentId = agentId,
            State = TaskState.Submitted,
            Messages = [new TaskMessageRecord(role, content, DateTime.UtcNow)],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _tasks[task.Id] = task;
        _logger.LogInformation("Created task {TaskId} for agent {AgentId}", task.Id, agentId);
        return Task.FromResult(task);
    }

    public Task<TaskRecord?> GetTaskAsync(string taskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return Task.FromResult<TaskRecord?>(null);
        if (task.IsExpired)
        {
            _tasks.TryRemove(taskId, out _);
            _logger.LogWarning("Task {TaskId} has expired", taskId);
            return Task.FromResult<TaskRecord?>(null);
        }
        return Task.FromResult<TaskRecord?>(task);
    }

    public Task<bool> UpdateTaskAsync(string taskId, TaskState state, string? errorMessage = null, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return Task.FromResult(false);
        task.State = state;
        task.UpdatedAt = DateTime.UtcNow;
        task.ErrorMessage = errorMessage;
        _logger.LogInformation("Task {TaskId} → {State}", taskId, state);
        return Task.FromResult(true);
    }

    public Task<bool> AppendMessageAsync(string taskId, string role, string content, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return Task.FromResult(false);
        task.Messages.Add(new TaskMessageRecord(role, content, DateTime.UtcNow));
        task.UpdatedAt = DateTime.UtcNow;
        task.LastMessageAt = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public async Task<bool> CompleteTaskAsync(string taskId, string role, string content, CancellationToken ct = default)
    {
        if (!await AppendMessageAsync(taskId, role, content, ct)) return false;
        return await UpdateTaskAsync(taskId, TaskState.Completed, null, ct);
    }

    public Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return Task.FromResult(false);
        if (task.State is TaskState.Completed or TaskState.Failed or TaskState.Canceled)
            return Task.FromResult(false);
        task.State = TaskState.Canceled;
        task.UpdatedAt = DateTime.UtcNow;
        _logger.LogInformation("Task {TaskId} cancelled", taskId);
        return Task.FromResult(true);
    }

    /// <summary>Number of tracked tasks (including not-yet-expired).</summary>
    public int Count => _tasks.Count;
}
