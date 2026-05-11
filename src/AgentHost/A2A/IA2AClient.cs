using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AgentHost.A2A;

/// <summary>
/// Contract for making Agent-to-Agent (A2A) protocol calls, supporting both HTTP and in-process routing.
/// </summary>
public interface IA2AClient
{
    /// <summary>Fetches the agent card from the given base URL.</summary>
    Task<AgentCard> FetchAgentCardAsync(Uri agentBaseUrl, CancellationToken ct = default);

    /// <summary>Sends a task synchronously and returns the completed <see cref="AgentTask"/>.</summary>
    Task<AgentTask> SendTaskAsync(Uri agentBaseUrl, A2ATaskSendRequest req, CancellationToken ct = default);

    /// <summary>
    /// Sends a task and streams <see cref="A2ATaskUpdate"/> events via SSE (HTTP) or direct iteration (in-process).
    /// </summary>
    IAsyncEnumerable<A2ATaskUpdate> SendTaskSubscribeAsync(Uri agentBaseUrl, A2ATaskSendRequest req, CancellationToken ct = default);
}

/// <summary>Request payload used by <see cref="IA2AClient"/> methods.</summary>
public class A2ATaskSendRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("message")]
    public required Message Message { get; set; }
}

/// <summary>A single update event from an A2A streaming call.</summary>
public class A2ATaskUpdate
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("status")]
    public TaskStatus? Status { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("artifact")]
    public Artifact? Artifact { get; set; }

    /// <summary>Set by the caller to identify the source agent when aggregating multi-agent results.</summary>
    [JsonIgnore]
    public string? SourceAgentId { get; set; }
}

/// <summary>Raised when the maximum cross-agent call depth is exceeded.</summary>
public sealed class A2ADepthExceededException(int depth, int maxDepth, string traceId)
    : Exception($"A2A_DEPTH_EXCEEDED: call depth {depth} >= max {maxDepth} (trace={traceId})")
{
    public int Depth { get; } = depth;
    public int MaxDepth { get; } = maxDepth;
    public string TraceId { get; } = traceId;
    public string ErrorCode => "A2A_DEPTH_EXCEEDED";
}

/// <summary>Raised when a cross-agent call would introduce a cycle (target agent already in the call path).</summary>
public sealed class A2ACycleDetectedException(string agentId, IReadOnlyList<string> path, string traceId)
    : Exception($"A2A_CYCLE_DETECTED: agent '{agentId}' already in path [{string.Join(" → ", path)}] (trace={traceId})")
{
    public string AgentId { get; } = agentId;
    public IReadOnlyList<string> Path { get; } = path;
    public string TraceId { get; } = traceId;
    public string ErrorCode => "A2A_CYCLE_DETECTED";
}
