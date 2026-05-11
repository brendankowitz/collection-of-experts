using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentHost.Orchestration;
using Microsoft.Extensions.Options;

namespace AgentHost.A2A;

/// <summary>
/// <see cref="IA2AClient"/> implementation that makes real HTTP calls to remote A2A endpoints.
/// Attaches call-context headers (<c>X-Trace-Id</c>, <c>X-Call-Depth</c>, <c>X-Call-Path</c>)
/// and parses SSE streams for streaming calls.
/// </summary>
public sealed class HttpA2AClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OrchestrationOptions> options,
    ILogger<HttpA2AClient> logger) : IA2AClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string ClientName = "a2a";

    public async Task<AgentCard> FetchAgentCardAsync(Uri agentBaseUrl, CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient(ClientName);
        var url = new Uri(agentBaseUrl, "/.well-known/agent-card.json");
        var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentCard>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Null agent card returned.");
    }

    public async Task<AgentTask> SendTaskAsync(Uri agentBaseUrl, A2ATaskSendRequest req, CancellationToken ct = default)
    {
        var agentId = InProcessA2AClient.ExtractAgentId(agentBaseUrl);
        var (ctx, request) = BuildRequest(agentBaseUrl, agentId, req, "/tasks/send");

        logger.LogInformation(
            "[A2A HTTP] depth={Depth} trace={TraceId} path=[{Path}] → {AgentId}",
            ctx.Depth, ctx.TraceId, string.Join(" → ", ctx.Path), agentId);

        var http = httpClientFactory.CreateClient(ClientName);
        using var scope = A2ACallContext.SetCurrent(ctx);
        var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var taskResponse = await response.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions, ct).ConfigureAwait(false);
        return taskResponse?.Task ?? throw new InvalidOperationException("Null task response.");
    }

    public async IAsyncEnumerable<A2ATaskUpdate> SendTaskSubscribeAsync(
        Uri agentBaseUrl, A2ATaskSendRequest req,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var agentId = InProcessA2AClient.ExtractAgentId(agentBaseUrl);
        var (ctx, request) = BuildRequest(agentBaseUrl, agentId, req, "/tasks/sendSubscribe");

        logger.LogInformation(
            "[A2A HTTP SSE] depth={Depth} trace={TraceId} path=[{Path}] → {AgentId}",
            ctx.Depth, ctx.TraceId, string.Join(" → ", ctx.Path), agentId);

        var http = httpClientFactory.CreateClient(ClientName);

        using var scope = A2ACallContext.SetCurrent(ctx);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var json = line[5..].Trim();
            if (string.IsNullOrEmpty(json)) continue;

            TaskEvent? evt;
            try { evt = JsonSerializer.Deserialize<TaskEvent>(json, JsonOptions); }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "[A2A HTTP SSE] Failed to parse SSE line: {Line}", line);
                continue;
            }

            if (evt is null) continue;

            yield return new A2ATaskUpdate
            {
                Event = evt.Event,
                Text = evt.Text,
                Status = evt.Status,
                Error = evt.Error,
                Artifact = evt.Artifact,
                SourceAgentId = agentId
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (A2ACallContext ctx, HttpRequestMessage request) BuildRequest(
        Uri agentBaseUrl, string agentId, A2ATaskSendRequest req, string path)
    {
        var ctx = A2ACallContext.Current;
        ValidateContext(ctx, agentId, options.Value.MaxCallDepth);
        var newCtx = ctx.Enter(agentId);

        var sendRequest = new SendTaskRequest
        {
            AgentId = agentId,
            SessionId = req.SessionId,
            Message = req.Message
        };

        var url = new Uri(agentBaseUrl, path);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(sendRequest)
        };
        newCtx.AddToHeaders(httpRequest);

        return (newCtx, httpRequest);
    }

    private static void ValidateContext(A2ACallContext ctx, string agentId, int maxDepth)
    {
        if (ctx.Depth >= maxDepth)
            throw new A2ADepthExceededException(ctx.Depth, maxDepth, ctx.TraceId);

        if (ctx.Path.Any(p => string.Equals(p, agentId, StringComparison.OrdinalIgnoreCase)))
            throw new A2ACycleDetectedException(agentId, ctx.Path, ctx.TraceId);
    }
}
