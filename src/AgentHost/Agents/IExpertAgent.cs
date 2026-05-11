using AgentHost.A2A;

namespace AgentHost.Agents;

/// <summary>
/// Contract implemented by every expert agent in the multi-agent system.
/// An expert agent owns knowledge about a specific repository or domain.
/// </summary>
public interface IExpertAgent
{
    /// <summary>Unique agent identifier (e.g., <c>fhir-server-expert</c>).</summary>
    string AgentId { get; }

    /// <summary>Human-readable name displayed in the UI.</summary>
    string Name { get; }

    /// <summary>Returns the A2A <see cref="AgentCard"/> describing this agent.</summary>
    AgentCard GetAgentCard();

    /// <summary>
    /// Processes a single user message and returns the complete response text.
    /// Use this for non-streaming (synchronous) interactions.
    /// </summary>
    /// <param name="message">The user's question or command.</param>
    /// <param name="sessionId">Session identifier for continuity.</param>
    /// <returns>The agent's response text.</returns>
    Task<string> ProcessMessageAsync(string message, string sessionId);

    /// <summary>
    /// Processes a user message and yields response chunks as an async stream.
    /// Use this for real-time (SSE) interactions.
    /// </summary>
    /// <param name="message">The user's question or command.</param>
    /// <param name="sessionId">Session identifier for continuity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of text chunks.</returns>
    IAsyncEnumerable<string> ProcessMessageStreamAsync(string message, string sessionId, CancellationToken ct);
}
