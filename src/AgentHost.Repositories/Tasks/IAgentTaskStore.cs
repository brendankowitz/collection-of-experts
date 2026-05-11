namespace AgentHost.Repositories.Tasks;

/// <summary>
/// Persistent store for A2A task instances supporting full lifecycle management.
/// State machine: Submitted → Working → Completed | Failed | Canceled | InputRequired.
/// </summary>
public interface IAgentTaskStore
{
    /// <summary>Creates and persists a new task.</summary>
    Task<TaskRecord> CreateTaskAsync(string sessionId, string agentId, string role, string content, CancellationToken ct = default);

    /// <summary>Returns the task by ID, or <c>null</c> if not found / expired.</summary>
    Task<TaskRecord?> GetTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>Transitions the task to a new status.</summary>
    Task<bool> UpdateTaskAsync(string taskId, TaskState state, string? errorMessage = null, CancellationToken ct = default);

    /// <summary>Appends a message to the task's conversation history.</summary>
    Task<bool> AppendMessageAsync(string taskId, string role, string content, CancellationToken ct = default);

    /// <summary>Adds the agent response message and marks the task Completed.</summary>
    Task<bool> CompleteTaskAsync(string taskId, string role, string content, CancellationToken ct = default);

    /// <summary>Cancels the task if it is not already in a terminal state.</summary>
    Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default);
}
