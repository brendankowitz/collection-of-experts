using System.Collections.Concurrent;

namespace AgentHost.A2A;

/// <summary>
/// In-memory store for <see cref="AgentTask"/> instances.
/// Tasks expire automatically after one hour and are removed on access.
/// Backed by <see cref="ConcurrentDictionary{TKey,TValue}"/> for thread-safety.
/// </summary>
public sealed class AgentTaskStore
{
    private readonly ConcurrentDictionary<string, AgentTask> _tasks = new();
    private readonly ILogger<AgentTaskStore> _logger;

    /// <summary>
    /// Creates a new <see cref="AgentTaskStore"/>.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public AgentTaskStore(ILogger<AgentTaskStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates and stores a new task.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="agentId">Target agent identifier.</param>
    /// <param name="initialMessage">The first user message.</param>
    /// <returns>The newly created task.</returns>
    public AgentTask CreateTask(string sessionId, string agentId, Message initialMessage)
    {
        var task = new AgentTask
        {
            SessionId = sessionId,
            AgentId = agentId,
            Status = TaskStatus.Submitted,
            Messages =
            [
                new Message
                {
                    Role = "user",
                    Parts = initialMessage.Parts.ToList()
                }
            ],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _tasks[task.Id] = task;
        _logger.LogInformation("Created task {TaskId} for agent {AgentId} in session {SessionId}",
            task.Id, agentId, sessionId);

        return task;
    }

    /// <summary>
    /// Retrieves a task by its unique identifier.
    /// Returns <c>null</c> if the task does not exist or has expired.
    /// </summary>
    /// <param name="taskId">The task GUID.</param>
    /// <returns>The task, or <c>null</c>.</returns>
    public AgentTask? GetTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return null;

        if (task.IsExpired)
        {
            _logger.LogWarning("Task {TaskId} has expired and is being removed", taskId);
            _tasks.TryRemove(taskId, out _);
            return null;
        }

        return task;
    }

    /// <summary>
    /// Updates the status and metadata of an existing task.
    /// </summary>
    /// <param name="taskId">The task GUID.</param>
    /// <param name="status">New status to assign.</param>
    /// <param name="errorMessage">Optional error message when status is <see cref="TaskStatus.Failed"/>.</param>
    /// <returns><c>true</c> if the task was found and updated.</returns>
    public bool UpdateTask(string taskId, TaskStatus status, string? errorMessage = null)
    {
        if (!_tasks.TryGetValue(taskId, out var task) || task.IsExpired)
        {
            if (task is { IsExpired: true })
                _tasks.TryRemove(taskId, out _);
            return false;
        }

        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;
        task.ErrorMessage = errorMessage;

        _logger.LogInformation("Updated task {TaskId} to status {Status}", taskId, status);
        return true;
    }

    /// <summary>
    /// Adds a response message to an existing task and marks it <see cref="TaskStatus.Completed"/>.
    /// </summary>
    /// <param name="taskId">The task GUID.</param>
    /// <param name="responseMessage">The agent's response message.</param>
    /// <returns><c>true</c> if the task was found and updated.</returns>
    public bool CompleteTask(string taskId, Message responseMessage)
    {
        if (!_tasks.TryGetValue(taskId, out var task) || task.IsExpired)
        {
            if (task is { IsExpired: true })
                _tasks.TryRemove(taskId, out _);
            return false;
        }

        task.Messages.Add(responseMessage);
        task.Status = TaskStatus.Completed;
        task.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation("Completed task {TaskId}", taskId);
        return true;
    }

    /// <summary>
    /// Cancels a task if it is not already in a terminal state.
    /// </summary>
    /// <param name="taskId">The task GUID.</param>
    /// <returns><c>true</c> if the task was found and cancelled.</returns>
    public bool CancelTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task) || task.IsExpired)
        {
            if (task is { IsExpired: true })
                _tasks.TryRemove(taskId, out _);
            return false;
        }

        if (task.Status is TaskStatus.Completed or TaskStatus.Failed or TaskStatus.Canceled)
            return false;

        task.Status = TaskStatus.Canceled;
        task.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation("Cancelled task {TaskId}", taskId);
        return true;
    }

    /// <summary>
    /// Returns the current count of tracked tasks (including expired ones until they are accessed).
    /// </summary>
    public int Count => _tasks.Count;
}
