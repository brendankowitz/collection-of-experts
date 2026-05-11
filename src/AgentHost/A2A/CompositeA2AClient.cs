using System.Runtime.CompilerServices;
using AgentHost.Orchestration;
using Microsoft.Extensions.Options;

namespace AgentHost.A2A;

/// <summary>
/// Routes A2A calls to either the <see cref="InProcessA2AClient"/> or <see cref="HttpA2AClient"/>
/// based on the target URI scheme and orchestration options:
/// <list type="bullet">
///   <item><c>inproc://agentId</c> → always in-process.</item>
///   <item>HTTP URL with localhost host → in-process when <see cref="OrchestrationOptions.InProcessCallsForLocalAgents"/> is true.</item>
///   <item>Everything else → HTTP.</item>
/// </list>
/// </summary>
public sealed class CompositeA2AClient(
    InProcessA2AClient inProcess,
    HttpA2AClient http,
    IOptions<OrchestrationOptions> options) : IA2AClient
{
    public Task<AgentCard> FetchAgentCardAsync(Uri agentBaseUrl, CancellationToken ct = default)
        => SelectClient(agentBaseUrl).FetchAgentCardAsync(agentBaseUrl, ct);

    public Task<AgentTask> SendTaskAsync(Uri agentBaseUrl, A2ATaskSendRequest req, CancellationToken ct = default)
        => SelectClient(agentBaseUrl).SendTaskAsync(agentBaseUrl, req, ct);

    public IAsyncEnumerable<A2ATaskUpdate> SendTaskSubscribeAsync(
        Uri agentBaseUrl, A2ATaskSendRequest req, CancellationToken ct = default)
        => SelectClient(agentBaseUrl).SendTaskSubscribeAsync(agentBaseUrl, req, ct);

    private IA2AClient SelectClient(Uri uri)
    {
        if (uri.Scheme.Equals("inproc", StringComparison.OrdinalIgnoreCase))
            return inProcess;

        if (options.Value.InProcessCallsForLocalAgents && IsLocal(uri))
            return inProcess;

        return http;
    }

    private static bool IsLocal(Uri uri)
    {
        var host = uri.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1";
    }
}
