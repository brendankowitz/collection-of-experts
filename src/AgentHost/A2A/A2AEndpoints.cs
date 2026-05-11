using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentHost.Agents;
using AgentHost.Repositories.Memory;
using AgentHost.Repositories.Tasks;
using Microsoft.AspNetCore.Mvc;

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
            var agentId = ResolveAgentId(ctx);
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
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.SendTask");
            logger.LogInformation("Received task send request for agent {AgentId}", request.AgentId);

            var agent = registry.GetAgent(request.AgentId ?? "");
            if (agent is null)
                return Results.BadRequest(new { error = $"Agent '{request.AgentId}' not found." });

            var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
            var userText = request.Message.Parts
                .OfType<TextPart>()
                .Select(tp => tp.Text)
                .FirstOrDefault() ?? "";

            var record = await taskStore.CreateTaskAsync(sessionId, agent.AgentId, request.Message.Role, userText);
            await taskStore.UpdateTaskAsync(record.Id, TaskState.Working);
            await agentMemory.RecordTurnAsync(agent.AgentId, sessionId, "user", userText);

            try
            {
                var responseText = await agent.ProcessMessageAsync(userText, sessionId);

                await taskStore.CompleteTaskAsync(record.Id, "agent", responseText);
                await agentMemory.RecordTurnAsync(agent.AgentId, sessionId, "agent", responseText);

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
            HttpContext ctx,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.SendSubscribe");
            logger.LogInformation("Received task subscribe request for agent {AgentId}", request.AgentId);

            var agent = registry.GetAgent(request.AgentId ?? "");
            if (agent is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { error = $"Agent '{request.AgentId}' not found." });
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

    private static string ResolveAgentId(HttpContext ctx)
    {
        var host = ctx.Request.Host.ToString();
        return host switch
        {
            var h when h.Contains("5001") => "fhir-server-expert",
            var h when h.Contains("5002") => "healthcare-components-expert",
            _ => ctx.Request.Query.TryGetValue("agentId", out var agentId)
                ? agentId.ToString()
                : "fhir-server-expert"
        };
    }

    private static async Task WriteSseEventAsync(StreamWriter writer, TaskEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt);
        await writer.WriteAsync($"data: {json}\n\n".AsMemory(), ct);
        await writer.FlushAsync(ct);
    }
}
