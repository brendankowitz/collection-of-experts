using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentHost.Agents;
using AgentHost.Observability;
using AgentHost.Orchestration;
using AgentHost.Repositories.Memory;
using AgentHost.Repositories.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgentHost.A2A;

/// <summary>
/// Extension methods that map A2A (Agent-to-Agent) protocol endpoints
/// onto an ASP.NET Core Minimal API <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class A2AEndpoints
{
    /// <summary>Maps all A2A protocol endpoints.</summary>
    public static IEndpointRouteBuilder MapA2AEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /.well-known/agent-card.json
        app.MapGet("/.well-known/agent-card.json", (
            AgentCardProvider cardProvider,
            HttpContext ctx,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.AgentCard");
            var agentId = ResolveAgentCardId(ctx);
            var card = cardProvider.GetCard(agentId);

            if (card is null)
            {
                logger.LogWarning("Agent card not found for {AgentId}", agentId);
                return Results.NotFound(new { error = $"Agent card for '{agentId}' not found." });
            }

            logger.LogInformation("Serving agent card for {AgentId}", agentId);
            return Results.Ok(card);
        })
        .WithName("GetAgentCard")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Retrieve A2A Agent Card metadata";
            operation.Description = "Returns the agent's capabilities, skills, and endpoint information per the A2A protocol.";
            return operation;
        });

        // POST /tasks/send
        app.MapPost("/tasks/send", async (
            [FromBody] SendTaskRequest request,
            IAgentTaskStore taskStore,
            IAgentMemory agentMemory,
            AgentRegistry registry,
            IOptions<OrchestrationOptions> opts,
            HttpContext ctx,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.SendTask");

            using var ctxScope = ApplyCallContextFromHeaders(ctx.Request.Headers);
            var effectiveAgentId = ResolveEffectiveAgentId(request.AgentId, opts.Value);
            logger.LogInformation("[{TraceId}] Received task send → agent={AgentId} depth={Depth}",
                A2ACallContext.Current?.TraceId, effectiveAgentId, A2ACallContext.Current?.Depth);

            if (A2ACallContext.Current is { } callCtx)
            {
                if (callCtx.Depth >= opts.Value.MaxCallDepth)
                    return Results.Json(new { error = "A2A_DEPTH_EXCEEDED", depth = callCtx.Depth }, statusCode: 429);
                if (callCtx.Path.Contains(effectiveAgentId, StringComparer.OrdinalIgnoreCase))
                    return Results.Json(new { error = "A2A_CYCLE_DETECTED", path = callCtx.Path }, statusCode: 409);
            }

            var agent = registry.GetAgent(effectiveAgentId);
            if (agent is null)
                return Results.BadRequest(new { error = $"Agent '{effectiveAgentId}' not found." });

            var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
            var userText = request.Message.Parts
                .OfType<TextPart>()
                .Select(tp => tp.Text)
                .FirstOrDefault() ?? "";

            var record = await taskStore.CreateTaskAsync(sessionId, agent.AgentId, request.Message.Role, userText);
            await taskStore.UpdateTaskAsync(record.Id, TaskState.Working);
            await agentMemory.RecordTurnAsync(agent.AgentId, sessionId, "user", userText);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var activity = AgentHostActivitySource.Source.StartActivity(
                "a2a.task.send",
                ActivityKind.Server);
            activity?.SetTag("agent.id", agent.AgentId);
            activity?.SetTag("task.id", record.Id);
            activity?.SetTag("session.id", sessionId);
            if (A2ACallContext.Current is { } ctx2)
                activity?.SetTag("a2a.call.depth", ctx2.Depth);

            AgentHostMetrics.TaskCount.Add(1, new TagList { { "agent", agent.AgentId }, { "operation", "send" } });

            try
            {
                var responseText = await agent.ProcessMessageAsync(userText, sessionId);

                await taskStore.CompleteTaskAsync(record.Id, "agent", responseText);
                await agentMemory.RecordTurnAsync(agent.AgentId, sessionId, "agent", responseText);

                sw.Stop();
                AgentHostMetrics.TaskDuration.Record(sw.Elapsed.TotalMilliseconds,
                    new TagList { { "agent", agent.AgentId }, { "status", "completed" } });

                _ = Task.Run(async () =>
                {
                    try { await agentMemory.SummarizeAndPersistAsync(agent.AgentId, sessionId); }
                    catch (Exception ex) { logger.LogError(ex, "SummarizeAndPersistAsync failed for task {TaskId}", record.Id); }
                });

                var completed = await taskStore.GetTaskAsync(record.Id);
                return Results.Ok(new TaskResponse { Task = ToAgentTask(completed ?? record) });
            }
            catch (Exception ex)
            {
                sw.Stop();
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                AgentHostMetrics.TaskDuration.Record(sw.Elapsed.TotalMilliseconds,
                    new TagList { { "agent", agent.AgentId }, { "status", "failed" } });
                logger.LogError(ex, "Task {TaskId} failed", record.Id);
                await taskStore.UpdateTaskAsync(record.Id, TaskState.Failed, ex.Message);
                return Results.StatusCode(500);
            }
        })
        .WithName("SendTask")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Send a task to an agent (synchronous)";
            operation.Description = "Creates a task, processes it, and returns the completed task response.";
            return operation;
        });

        // POST /tasks/sendSubscribe
        app.MapPost("/tasks/sendSubscribe", async (
            [FromBody] SendTaskRequest request,
            IAgentTaskStore taskStore,
            IAgentMemory agentMemory,
            AgentRegistry registry,
            IOptions<OrchestrationOptions> opts,
            HttpContext ctx,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.SendSubscribe");

            using var ctxScope = ApplyCallContextFromHeaders(ctx.Request.Headers);
            var effectiveAgentId = ResolveEffectiveAgentId(request.AgentId, opts.Value);
            logger.LogInformation("[{TraceId}] Received subscribe → agent={AgentId} depth={Depth}",
                A2ACallContext.Current?.TraceId, effectiveAgentId, A2ACallContext.Current?.Depth);

            if (A2ACallContext.Current is { } callCtx)
            {
                if (callCtx.Depth >= opts.Value.MaxCallDepth)
                {
                    ctx.Response.StatusCode = 429;
                    await ctx.Response.WriteAsJsonAsync(new { error = "A2A_DEPTH_EXCEEDED", depth = callCtx.Depth }, ct);
                    return;
                }
                if (callCtx.Path.Contains(effectiveAgentId, StringComparer.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 409;
                    await ctx.Response.WriteAsJsonAsync(new { error = "A2A_CYCLE_DETECTED", path = callCtx.Path }, ct);
                    return;
                }
            }

            var agent = registry.GetAgent(effectiveAgentId);
            if (agent is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = $"Agent '{effectiveAgentId}' not found." });
                return;
            }

            var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
            var userText = request.Message.Parts
                .OfType<TextPart>()
                .Select(tp => tp.Text)
                .FirstOrDefault() ?? "";

            var record = await taskStore.CreateTaskAsync(sessionId, agent.AgentId, request.Message.Role, userText, ct);

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            await using var writer = new StreamWriter(ctx.Response.Body);

            await WriteSseEventAsync(writer, new TaskEvent { Event = "status", Status = TaskStatus.Working }, ct);
            await agentMemory.RecordTurnAsync(agent.AgentId, sessionId, "user", userText, ct);

            var collectedResponse = new System.Text.StringBuilder();
            try
            {
                await foreach (var chunk in agent.ProcessMessageStreamAsync(userText, sessionId, ct))
                {
                    collectedResponse.Append(chunk);
                    await WriteSseEventAsync(writer, new TaskEvent { Event = "text", Text = chunk }, ct);
                }

                await taskStore.UpdateTaskAsync(record.Id, TaskState.Completed, ct: ct);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "status", Status = TaskStatus.Completed }, ct);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "done" }, ct);

                var agentResponse = collectedResponse.ToString();
                await agentMemory.RecordTurnAsync(agent.AgentId, sessionId, "agent", agentResponse, ct);

                _ = Task.Run(async () =>
                {
                    try { await agentMemory.SummarizeAndPersistAsync(agent.AgentId, sessionId); }
                    catch (Exception ex) { logger.LogError(ex, "SummarizeAndPersistAsync failed for task {TaskId}", record.Id); }
                });
            }
            catch (OperationCanceledException)
            {
                await taskStore.UpdateTaskAsync(record.Id, TaskState.Canceled, ct: ct);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "status", Status = TaskStatus.Canceled }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Streaming task {TaskId} failed", record.Id);
                await taskStore.UpdateTaskAsync(record.Id, TaskState.Failed, ex.Message, ct);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "error", Error = ex.Message }, ct);
            }

            await writer.FlushAsync(ct);
        })
        .WithName("SendTaskSubscribe")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Send a task to an agent (SSE streaming)";
            operation.Description = "Creates a task and returns the response as a Server-Sent Events stream.";
            return operation;
        });

        // GET /tasks/{taskId}
        app.MapGet("/tasks/{taskId}", async (
            string taskId,
            IAgentTaskStore taskStore,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.GetTask");
            logger.LogInformation("Retrieving task {TaskId}", taskId);

            var record = await taskStore.GetTaskAsync(taskId);
            if (record is null)
                return Results.NotFound(new { error = $"Task '{taskId}' not found or expired." });

            return Results.Ok(new TaskResponse { Task = ToAgentTask(record) });
        })
        .WithName("GetTask")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get task status and result";
            operation.Description = "Returns the current state of a task, including messages and artifacts.";
            return operation;
        });

        // POST /tasks/{taskId}/cancel
        app.MapPost("/tasks/{taskId}/cancel", async (
            string taskId,
            IAgentTaskStore taskStore,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.CancelTask");
            logger.LogInformation("Cancelling task {TaskId}", taskId);

            var cancelled = await taskStore.CancelTaskAsync(taskId);
            if (!cancelled)
                return Results.BadRequest(new { error = $"Task '{taskId}' not found or already in a terminal state." });

            return Results.Ok(new { taskId, status = TaskStatus.Canceled.ToString() });
        })
        .WithName("CancelTask")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Cancel a running task";
            operation.Description = "Cancels a task that is in Submitted, Working, or InputRequired state.";
            return operation;
        });

        return app;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Maps a repository <see cref="TaskRecord"/> to the A2A protocol <see cref="AgentTask"/>.</summary>
    private static AgentTask ToAgentTask(TaskRecord r) => new()
    {
        Id = r.Id,
        AgentId = r.AgentId,
        SessionId = r.SessionId,
        Status = r.State switch
        {
            TaskState.Submitted     => TaskStatus.Submitted,
            TaskState.Working       => TaskStatus.Working,
            TaskState.InputRequired => TaskStatus.InputRequired,
            TaskState.Completed     => TaskStatus.Completed,
            TaskState.Failed        => TaskStatus.Failed,
            TaskState.Canceled      => TaskStatus.Canceled,
            _                       => TaskStatus.Failed
        },
        Messages = r.Messages
            .Select(m => new Message
            {
                Role = m.Role,
                Parts = [new TextPart { Text = m.Content }]
            })
            .ToList(),
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        ErrorMessage = r.ErrorMessage
    };

    /// <summary>
    /// Determines the agent ID to use for task routing.
    /// Defaults to "coordinator" when no explicit agent is specified and coordinator is enabled.
    /// </summary>
    internal static string ResolveEffectiveAgentId(string? requestedAgentId, OrchestrationOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(requestedAgentId) &&
            !requestedAgentId.Equals("coordinator", StringComparison.OrdinalIgnoreCase))
            return requestedAgentId;

        if (opts.EnableCoordinator)
            return "coordinator";

        return requestedAgentId ?? "coordinator";
    }

    /// <summary>Parses incoming A2A call-context headers and pushes them as the current <see cref="A2ACallContext"/>.</summary>
    internal static IDisposable ApplyCallContextFromHeaders(IHeaderDictionary headers)
    {
        var traceId = headers.TryGetValue("X-Trace-Id", out var t) ? t.ToString() : null;
        var depth = headers.TryGetValue("X-Call-Depth", out var d) ? d.ToString() : null;
        var path = headers.TryGetValue("X-Call-Path", out var p) ? p.ToString() : null;

        if (traceId is null && depth is null && path is null)
            return Disposable.Empty;

        var ctx = A2ACallContext.FromHeaders(traceId, depth, path);
        return A2ACallContext.SetCurrent(ctx);
    }

    private static string ResolveAgentCardId(HttpContext ctx)
    {
        if (ctx.Request.Query.TryGetValue("agent", out var agentParam) &&
            !string.IsNullOrWhiteSpace(agentParam))
            return agentParam.ToString();

        var host = ctx.Request.Host.ToString();
        return host switch
        {
            var h when h.Contains("5001") => "fhir-server-expert",
            var h when h.Contains("5002") => "healthcare-components-expert",
            _ => ctx.Request.Query.TryGetValue("agentId", out var agentId)
                ? agentId.ToString()
                : "coordinator"
        };
    }

    private static async Task WriteSseEventAsync(StreamWriter writer, TaskEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt);
        await writer.WriteAsync($"data: {json}\n\n".AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    private static class Disposable
    {
        public static readonly IDisposable Empty = new NullDisposable();
        private sealed class NullDisposable : IDisposable { public void Dispose() { } }
    }
}
