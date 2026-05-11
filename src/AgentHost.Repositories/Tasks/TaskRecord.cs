namespace AgentHost.Repositories.Tasks;

/// <summary>A2A task lifecycle states stored in the repository.</summary>
public enum TaskState
{
    Submitted,
    Working,
    InputRequired,
    Completed,
    Failed,
    Canceled
}

/// <summary>A message turn stored alongside a task.</summary>
public sealed record TaskMessageRecord(string Role, string Content, DateTime CreatedAt);

/// <summary>Repository-layer task record, independent of A2A protocol models.</summary>
public sealed class TaskRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AgentId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public TaskState State { get; set; } = TaskState.Submitted;
    public List<TaskMessageRecord> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }

    /// <summary>Whether this task has exceeded the 1-hour TTL.</summary>
    public bool IsExpired => DateTime.UtcNow - CreatedAt > TimeSpan.FromHours(1);
}
