using AgentHost.A2A;
using AgentHost.Agents;
using Microsoft.AspNetCore.SignalR;

namespace AgentHost.Hubs;

/// <summary>
/// SignalR hub for real-time web chat with expert agents.
/// Provides methods for sending messages (sync and streaming) and
/// discovering available agents.
/// </summary>
public class ChatHub : Hub
{
    private readonly AgentRegistry _registry;
    private readonly ILogger<ChatHub> _logger;

    /// <summary>
    /// Creates a new <see cref="ChatHub"/>.
    /// </summary>
    public ChatHub(AgentRegistry registry, ILogger<ChatHub> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Sends a message to the specified agent and returns the complete response.
    /// </summary>
    /// <param name="agentId">Target agent identifier.</param>
    /// <param name="message">User message.</param>
    /// <param name="sessionId">Session identifier for continuity.</param>
    /// <returns>The agent's full response text.</returns>
    public async Task<string> SendMessage(string agentId, string message, string sessionId)
    {
        _logger.LogInformation("[ChatHub] SendMessage from connection {ConnectionId} to agent {AgentId} in session {SessionId}",
            Context.ConnectionId, agentId, sessionId);

        var agent = _registry.GetAgent(agentId);
        if (agent is null)
        {
            _logger.LogWarning("[ChatHub] Agent {AgentId} not found, routing automatically", agentId);
            agent = _registry.RouteToAgent(message);
        }

        var response = await agent.ProcessMessageAsync(message, sessionId);

        // Also broadcast to the session group so other clients in the same session see it
        await Clients.Group(sessionId).SendAsync("MessageReceived", new
        {
            agentId = agent.AgentId,
            agentName = agent.Name,
            message,
            response,
            timestamp = DateTime.UtcNow
        });

        return response;
    }

    /// <summary>
    /// Sends a message to the specified agent and streams the response
    /// back to the client in real-time chunks.
    /// </summary>
    /// <param name="agentId">Target agent identifier.</param>
    /// <param name="message">User message.</param>
    /// <param name="sessionId">Session identifier for continuity.</param>
    /// <returns>Async enumerable of text chunks.</returns>
    public async IAsyncEnumerable<string> StreamMessage(string agentId, string message, string sessionId)
    {
        _logger.LogInformation("[ChatHub] StreamMessage from connection {ConnectionId} to agent {AgentId} in session {SessionId}",
            Context.ConnectionId, agentId, sessionId);

        var agent = _registry.GetAgent(agentId);
        if (agent is null)
        {
            _logger.LogWarning("[ChatHub] Agent {AgentId} not found, routing automatically", agentId);
            agent = _registry.RouteToAgent(message);
        }

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await foreach (var chunk in agent.ProcessMessageStreamAsync(message, sessionId, cts.Token))
        {
            yield return chunk;
        }

        // Notify session group that streaming completed
        await Clients.Group(sessionId).SendAsync("StreamCompleted", new
        {
            agentId = agent.AgentId,
            agentName = agent.Name,
            message,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Returns a list of all available agents with their metadata.
    /// </summary>
    public Task<List<object>> GetAgents()
    {
        var agents = _registry.GetAllAgents().Select(a => new
        {
            a.AgentId,
            a.Name,
            card = a.GetAgentCard()
        }).ToList<object>();

        _logger.LogInformation("[ChatHub] Returning {Count} agents to connection {ConnectionId}",
            agents.Count, Context.ConnectionId);

        return Task.FromResult(agents);
    }

    /// <summary>
    /// Joins a session group so the client receives messages for that session.
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("[ChatHub] Connection {ConnectionId} joined session {SessionId}",
            Context.ConnectionId, sessionId);
    }

    /// <summary>
    /// Leaves a session group.
    /// </summary>
    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        _logger.LogInformation("[ChatHub] Connection {ConnectionId} left session {SessionId}",
            Context.ConnectionId, sessionId);
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("[ChatHub] Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[ChatHub] Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
