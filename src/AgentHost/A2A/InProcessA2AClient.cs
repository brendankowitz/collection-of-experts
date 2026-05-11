using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentHost.Agents;
using AgentHost.Orchestration;
using Microsoft.Extensions.Options;

namespace AgentHost.A2A;

/// <summary>
/// <see cref="IA2AClient"/> implementation that calls agents within the same process via
/// <see cref="AgentRegistry"/>. Selected when the target URI uses scheme <c>inproc://</c>
/// or when the host is localhost and <see cref="OrchestrationOptions.InProcessCallsForLocalAgents"/> is true.
/// </summary>
public sealed class InProcessA2AClient(
    Lazy<AgentRegistry> registry,
    IOptions<OrchestrationOptions> options,
    ILogger<InProcessA2AClient> logger) : IA2AClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<AgentCard> FetchAgentCardAsync(Uri agentBaseUrl, CancellationToken ct = default)
    {
        var agentId = ExtractAgentId(agentBaseUrl);
        var agent = registry.Value.GetAgent(agentId)
            ?? throw new InvalidOperationException($"Agent '{agentId}' not found in local registry.");
        return Task.FromResult(agent.GetAgentCard());
    }

    public async Task<AgentTask> SendTaskAsync(Uri agentBaseUrl, A2ATaskSendRequest req, CancellationToken ct = default)
    {
        var agentId = ExtractAgentId(agentBaseUrl);
        var ctx = EnterCall(agentId);
        var userText = GetUserText(req);

        logger.LogInformation(
            "[A2A InProc] depth={Depth} trace={TraceId} path=[{Path}] → {AgentId}",
            ctx.Depth, ctx.TraceId, string.Join(" → ", ctx.Path), agentId);

        var agent = registry.Value.GetAgent(agentId)
            ?? throw new InvalidOperationException($"Agent '{agentId}' not found.");

        using var scope = A2ACallContext.SetCurrent(ctx);
        var response = await agent.ProcessMessageAsync(userText, req.SessionId ?? Guid.NewGuid().ToString("N"))
            .ConfigureAwait(false);

        return new AgentTask
        {
            AgentId = agentId,
            SessionId = req.SessionId,
            Status = TaskStatus.Completed,
            Messages =
            [
                new Message { Role = "user", Parts = [new TextPart { Text = userText }] },
                new Message { Role = "agent", Parts = [new TextPart { Text = response }] }
            ]
        };
    }

    public async IAsyncEnumerable<A2ATaskUpdate> SendTaskSubscribeAsync(
        Uri agentBaseUrl, A2ATaskSendRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agentId = ExtractAgentId(agentBaseUrl);
        var ctx = EnterCall(agentId);
        var userText = GetUserText(req);

        logger.LogInformation(
            "[A2A InProc SSE] depth={Depth} trace={TraceId} path=[{Path}] → {AgentId}",
            ctx.Depth, ctx.TraceId, string.Join(" → ", ctx.Path), agentId);

        var agent = registry.Value.GetAgent(agentId)
            ?? throw new InvalidOperationException($"Agent '{agentId}' not found.");

        yield return new A2ATaskUpdate { Event = "status", Status = TaskStatus.Working, SourceAgentId = agentId };

        using var scope = A2ACallContext.SetCurrent(ctx);
        await foreach (var chunk in agent.ProcessMessageStreamAsync(
                userText, req.SessionId ?? Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false))
        {
            yield return new A2ATaskUpdate { Event = "text", Text = chunk, SourceAgentId = agentId };
        }

        yield return new A2ATaskUpdate { Event = "status", Status = TaskStatus.Completed, SourceAgentId = agentId };
        yield return new A2ATaskUpdate { Event = "done", SourceAgentId = agentId };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Validates depth and cycle constraints, returns the new context.</summary>
    private A2ACallContext EnterCall(string agentId)
    {
        var ctx = A2ACallContext.Current;
        ValidateContext(ctx, agentId, options.Value.MaxCallDepth);
        return ctx.Enter(agentId);
    }

    private static void ValidateContext(A2ACallContext ctx, string agentId, int maxDepth)
    {
        if (ctx.Depth >= maxDepth)
            throw new A2ADepthExceededException(ctx.Depth, maxDepth, ctx.TraceId);

        if (ctx.Path.Any(p => string.Equals(p, agentId, StringComparison.OrdinalIgnoreCase)))
            throw new A2ACycleDetectedException(agentId, ctx.Path, ctx.TraceId);
    }

    /// <summary>Extracts the agent ID from the URI (host component for inproc://, host:port for HTTP).</summary>
    internal static string ExtractAgentId(Uri uri)
        => uri.Scheme.Equals("inproc", StringComparison.OrdinalIgnoreCase)
            ? uri.Host
            : uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static string GetUserText(A2ATaskSendRequest req)
        => req.Message.Parts.OfType<TextPart>().FirstOrDefault()?.Text ?? "";
}
