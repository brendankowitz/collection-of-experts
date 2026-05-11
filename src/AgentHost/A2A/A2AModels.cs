using System.Text.Json.Serialization;

namespace AgentHost.A2A;

/// <summary>
/// Represents the metadata card for an A2A agent.
/// Published at /.well-known/agent-card.json per the A2A protocol.
/// </summary>
public class AgentCard
{
    /// <summary>Name of the agent.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Human-readable description of the agent's purpose.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    /// <summary>Agent version string (SemVer).</summary>
    [JsonPropertyName("version")]
    public required string Version { get; set; }

    /// <summary>Base URL where the agent is hosted.</summary>
    [JsonPropertyName("url")]
    public required string Url { get; set; }

    /// <summary>Capabilities this agent supports.</summary>
    [JsonPropertyName("capabilities")]
    public AgentCapabilities Capabilities { get; set; } = new();

    /// <summary>Skills this agent can perform.</summary>
    [JsonPropertyName("skills")]
    public List<AgentSkill> Skills { get; set; } = [];

    /// <summary>Unique identifier for the agent.</summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }
}

/// <summary>
/// Capabilities supported by an A2A agent.
/// </summary>
public class AgentCapabilities
{
    /// <summary>Whether the agent supports SSE streaming responses.</summary>
    [JsonPropertyName("streaming")]
    public bool Streaming { get; set; }
}

/// <summary>
/// A skill that an agent can perform.
/// </summary>
public class AgentSkill
{
    /// <summary>Unique identifier for the skill.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>Name of the skill.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Description of what the skill does.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    /// <example>Example query that can be handled by this skill.</example>
    [JsonPropertyName("exampleQueries")]
    public List<string> ExampleQueries { get; set; } = [];
}

/// <summary>
/// Lifecycle states for an A2A task.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskStatus
{
    Submitted,
    Working,
    InputRequired,
    Completed,
    Failed,
    Canceled
}

/// <summary>
/// Represents a task in the A2A protocol with full lifecycle tracking.
/// </summary>
public class AgentTask
{
    /// <summary>Unique task identifier (GUID).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Current status of the task.</summary>
    [JsonPropertyName("status")]
    public TaskStatus Status { get; set; } = TaskStatus.Submitted;

    /// <summary>Messages exchanged during the task.</summary>
    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; } = [];

    /// <summary>Response artifacts produced by the task.</summary>
    [JsonPropertyName("artifacts")]
    public List<Artifact> Artifacts { get; set; } = [];

    /// <summary>Session identifier for grouping related tasks.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>ID of the agent handling this task.</summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>When the task was created (UTC).</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the task was last updated (UTC).</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Error message if the task failed.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>Whether the task has expired (> 1 hour old).</summary>
    [JsonIgnore]
    public bool IsExpired => DateTime.UtcNow - CreatedAt > TimeSpan.FromHours(1);
}

/// <summary>
/// A message in the A2A protocol, consisting of one or more parts.
/// </summary>
public class Message
{
    /// <summary>Role of the message sender.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    /// <summary>Parts that make up the message content.</summary>
    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; } = [];
}

/// <summary>
/// Base class for message parts in the A2A protocol.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(FilePart), "file")]
public abstract class Part
{
}

/// <summary>
/// A text part within an A2A message.
/// </summary>
public class TextPart : Part
{
    /// <summary>The text content.</summary>
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// A file part within an A2A message.
/// </summary>
public class FilePart : Part
{
    /// <summary>Name of the file.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>MIME type of the file.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>Base64-encoded file content.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>URI where the file can be fetched.</summary>
    [JsonPropertyName("uri")]
    public string? Uri { get; set; }
}

/// <summary>
/// An artifact produced as the result of a task.
/// </summary>
public class Artifact
{
    /// <summary>Name of the artifact.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Description of the artifact.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Parts that make up the artifact content.</summary>
    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; } = [];
}

/// <summary>
/// Request payload for creating and sending a task.
/// </summary>
public class SendTaskRequest
{
    /// <summary>Unique identifier for the session.</summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    /// <summary>ID of the target agent.</summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>The user's message to process.</summary>
    [JsonPropertyName("message")]
    public required Message Message { get; set; }

    /// <summary>Whether to use streaming mode.</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

/// <summary>
/// Response wrapper for task operations.
/// </summary>
public class TaskResponse
{
    /// <summary>The task, including current status and any artifacts.</summary>
    [JsonPropertyName("task")]
    public required AgentTask Task { get; set; }
}

/// <summary>
/// SSE event sent during streaming task execution.
/// </summary>
public class TaskEvent
{
    /// <summary>Event type: status, text, artifact, error, done.</summary>
    [JsonPropertyName("event")]
    public required string Event { get; set; }

    /// <summary>Text content (for text events).</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Current task status (for status events).</summary>
    [JsonPropertyName("status")]
    public TaskStatus? Status { get; set; }

    /// <summary>Error message (for error events).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Associated artifact (for artifact events).</summary>
    [JsonPropertyName("artifact")]
    public Artifact? Artifact { get; set; }
}
