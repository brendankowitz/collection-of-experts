using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentHost.Agents;
using Microsoft.AspNetCore.Mvc;

namespace AgentHost.A2A;

/// <summary>
/// Extension methods that map A2A (Agent-to-Agent) protocol endpoints
/// onto an ASP.NET Core Minimal API <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class A2AEndpoints
{
    /// <summary>
    /// Maps all A2A protocol endpoints:
    /// <list type="bullet">
    ///   <item><c>GET /.well-known/agent-card.json</c> – agent metadata</item>
    ///   <item><c>POST /tasks/send</c> – create task (sync response)</item>
    ///   <item><c>POST /tasks/sendSubscribe</c> – create task (SSE stream)</item>
    ///   <item><c>GET /tasks/{taskId}</c> – get task status</item>
    ///   <item><c>POST /tasks/{taskId}/cancel</c> – cancel task</item>
    /// </list>
    /// </summary>
    public static IEndpointRouteBuilder MapA2AEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /.well-known/agent-card.json
        app.MapGet("/.well-known/agent-card.json", (
            AgentCardProvider cardProvider,
            HttpContext ctx,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.AgentCard");
            // Determine which agent is being requested based on the Host header / port
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
            AgentTaskStore taskStore,
            AgentRegistry registry,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.SendTask");
            logger.LogInformation("Received task send request for agent {AgentId}", request.AgentId);

            var agent = registry.GetAgent(request.AgentId ?? "");
            if (agent is null)
                return Results.BadRequest(new { error = $"Agent '{request.AgentId}' not found." });

            var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");
            var task = taskStore.CreateTask(sessionId, agent.AgentId, request.Message);

            // Update status to Working while processing
            taskStore.UpdateTask(task.Id, TaskStatus.Working);

            try
            {
                var userText = request.Message.Parts
                    .OfType<TextPart>()
                    .Select(tp => tp.Text)
                    .FirstOrDefault() ?? "";

                var responseText = await agent.ProcessMessageAsync(userText, sessionId);

                var responseMessage = new Message
                {
                    Role = "agent",
                    Parts =
                    [
                        new TextPart { Text = responseText }
                    ]
                };

                taskStore.CompleteTask(task.Id, responseMessage);

                return Results.Ok(new TaskResponse { Task = taskStore.GetTask(task.Id)! });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Task {TaskId} failed", task.Id);
                taskStore.UpdateTask(task.Id, TaskStatus.Failed, ex.Message);
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
            AgentTaskStore taskStore,
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
            var task = taskStore.CreateTask(sessionId, agent.AgentId, request.Message);

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            await using var writer = new StreamWriter(ctx.Response.Body);

            // Send initial status
            await WriteSseEventAsync(writer, new TaskEvent { Event = "status", Status = TaskStatus.Working }, ct);

            try
            {
                var userText = request.Message.Parts
                    .OfType<TextPart>()
                    .Select(tp => tp.Text)
                    .FirstOrDefault() ?? "";

                await foreach (var chunk in agent.ProcessMessageStreamAsync(userText, sessionId, ct))
                {
                    await WriteSseEventAsync(writer, new TaskEvent { Event = "text", Text = chunk }, ct);
                }

                taskStore.UpdateTask(task.Id, TaskStatus.Completed);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "status", Status = TaskStatus.Completed }, ct);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "done" }, ct);
            }
            catch (OperationCanceledException)
            {
                taskStore.UpdateTask(task.Id, TaskStatus.Canceled);
                await WriteSseEventAsync(writer, new TaskEvent { Event = "status", Status = TaskStatus.Canceled }, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Streaming task {TaskId} failed", task.Id);
                taskStore.UpdateTask(task.Id, TaskStatus.Failed, ex.Message);
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
        app.MapGet("/tasks/{taskId}", (
            string taskId,
            AgentTaskStore taskStore,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.GetTask");
            logger.LogInformation("Retrieving task {TaskId}", taskId);

            var task = taskStore.GetTask(taskId);
            if (task is null)
                return Results.NotFound(new { error = $"Task '{taskId}' not found or expired." });

            return Results.Ok(new TaskResponse { Task = task });
        })
        .WithName("GetTask")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get task status and result";
            operation.Description = "Returns the current state of a task, including messages and artifacts.";
            return operation;
        });

        // POST /tasks/{taskId}/cancel
        app.MapPost("/tasks/{taskId}/cancel", (
            string taskId,
            AgentTaskStore taskStore,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("A2A.CancelTask");
            logger.LogInformation("Cancelling task {TaskId}", taskId);

            var cancelled = taskStore.CancelTask(taskId);
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

    /// <summary>
    /// Resolves the agent ID from the HTTP context (Host header / port).
    /// </summary>
    private static string ResolveAgentId(HttpContext ctx)
    {
        var host = ctx.Request.Host.ToString();

        return host switch
        {
            var h when h.Contains("5001") => "fhir-server-expert",
            var h when h.Contains("5002") => "healthcare-components-expert",
            _ => ctx.Request.Query.TryGetValue("agentId", out var agentId)
                ? agentId.ToString()
                : "fhir-server-expert" // default
        };
    }

    /// <summary>
    /// Writes a single SSE event to the response stream.
    /// </summary>
    private static async Task WriteSseEventAsync(StreamWriter writer, TaskEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt);
        await writer.WriteAsync($"data: {json}\n\n".AsMemory(), ct);
        await writer.FlushAsync(ct);
    }
}
