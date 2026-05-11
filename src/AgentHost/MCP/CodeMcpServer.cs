using System.Text.Json;
using System.Text.Json.Nodes;
using AgentHost.Agents;
using AgentHost.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentHost.MCP;

/// <summary>
/// MCP (Model Context Protocol) compatible tool server.
/// Exposes code-search and file-content tools that can be consumed
/// by MCP clients such as Claude Desktop or other AI assistants.
/// </summary>
public static class CodeMcpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Maps MCP tool endpoints onto the ASP.NET Core route table.
    /// </summary>
    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder app)
    {
        // MCP protocol endpoint (list tools)
        app.MapGet("/mcp/tools", (ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("MCP");
            logger.LogInformation("Listing MCP tools");

            var tools = new object[]
            {
                new
                {
                    name = "search_code",
                    description = "Search a repository for files matching a query",
                    parameters = new
                    {
                        type = "object",
                        required = new[] { "repo", "query" },
                        properties = new
                        {
                            repo = new { type = "string", description = "Repository name: 'fhir-server' or 'healthcare-shared-components'" },
                            query = new { type = "string", description = "Search query (keywords)" },
                            topK = new { type = "integer", description = "Maximum results to return (default 5)" }
                        }
                    }
                },
                new
                {
                    name = "get_file_content",
                    description = "Retrieve the full content of a file from a repository",
                    parameters = new
                    {
                        type = "object",
                        required = new[] { "repo", "filePath" },
                        properties = new
                        {
                            repo = new { type = "string", description = "Repository name" },
                            filePath = new { type = "string", description = "File path within the repository" }
                        }
                    }
                },
                new
                {
                    name = "explain_architecture",
                    description = "Get an architecture overview of a system component",
                    parameters = new
                    {
                        type = "object",
                        required = new[] { "component" },
                        properties = new
                        {
                            component = new
                            {
                                type = "string",
                                description = "Component name: 'fhir-search', 'fhir-export', 'fhir-auth', 'sql-wrapper', 'blob-client', 'mediator', 'health-checks', 'change-feed'"
                            }
                        }
                    }
                },
                new
                {
                    name = "create_pr",
                    description = "Get guidance for creating a pull request",
                    parameters = new
                    {
                        type = "object",
                        required = new[] { "repo" },
                        properties = new
                        {
                            repo = new { type = "string", description = "Repository name" },
                            title = new { type = "string", description = "PR title" },
                            description = new { type = "string", description = "PR description" }
                        }
                    }
                },
                new
                {
                    name = "list_agents",
                    description = "List all available expert agents",
                    parameters = new
                    {
                        type = "object",
                        required = Array.Empty<string>(),
                        properties = new { }
                    }
                },
                new
                {
                    name = "ask_agent",
                    description = "Send a question to a specific expert agent",
                    parameters = new
                    {
                        type = "object",
                        required = new[] { "agentId", "message" },
                        properties = new
                        {
                            agentId = new { type = "string", description = "Agent identifier (e.g., 'fhir-server-expert')" },
                            message = new { type = "string", description = "Question or command for the agent" }
                        }
                    }
                }
            };

            return Results.Ok(new { tools });
        })
        .WithName("McpListTools")
        .WithOpenApi(operation =>
        {
            operation.Summary = "List available MCP tools";
            operation.Description = "Returns metadata about all tools exposed by this MCP server.";
            return operation;
        });

        // MCP tool execution endpoint
        app.MapPost("/mcp/tools/call", async (
            [FromBody] JsonObject request,
            MockCodeIndexService codeIndex,
            AgentRegistry registry,
            IServiceProvider services,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("MCP");
            var toolName = request["name"]?.GetValue<string>() ?? "";
            var arguments = request["arguments"] as JsonObject ?? new JsonObject();

            logger.LogInformation("MCP tool called: {ToolName}", toolName);

            try
            {
                var result = toolName switch
                {
                    "search_code" => await HandleSearchCodeAsync(arguments, codeIndex),
                    "get_file_content" => await HandleGetFileContentAsync(arguments, codeIndex),
                    "explain_architecture" => await HandleExplainArchitectureAsync(arguments, registry),
                    "create_pr" => await HandleCreatePrAsync(arguments),
                    "list_agents" => await HandleListAgentsAsync(registry),
                    "ask_agent" => await HandleAskAgentAsync(arguments, registry),
                    _ => new { error = $"Unknown tool: '{toolName}'" }
                };

                return Results.Ok(new { result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MCP tool {ToolName} failed", toolName);
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("McpCallTool")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Execute an MCP tool";
            operation.Description = "Invokes the specified tool with the provided arguments.";
            return operation;
        });

        return app;
    }

    // ─── Tool Handlers ───────────────────────────────────────────────────────

    private static Task<object> HandleSearchCodeAsync(JsonObject args, MockCodeIndexService codeIndex)
    {
        var repo = args["repo"]?.GetValue<string>() ?? "fhir-server";
        var query = args["query"]?.GetValue<string>() ?? "";
        var topK = args["topK"]?.GetValue<int>() ?? 5;

        var results = codeIndex.Search(repo, query);
        var limited = results.Take(topK).Select(r => new
        {
            filePath = r.FilePath,
            snippet = r.Snippet.Length > 500 ? r.Snippet[..500] + "..." : r.Snippet
        }).ToList();

        return Task.FromResult<object>(new
        {
            repo,
            query,
            totalResults = results.Count,
            returned = limited.Count,
            files = limited
        });
    }

    private static Task<object> HandleGetFileContentAsync(JsonObject args, MockCodeIndexService codeIndex)
    {
        var repo = args["repo"]?.GetValue<string>() ?? "";
        var filePath = args["filePath"]?.GetValue<string>() ?? "";

        var content = codeIndex.GetFileContent(repo, filePath);

        if (content is null)
            return Task.FromResult<object>(new { repo, filePath, found = false, error = "File not found" });

        return Task.FromResult<object>(new
        {
            repo,
            filePath,
            found = true,
            content
        });
    }

    private static async Task<object> HandleExplainArchitectureAsync(JsonObject args, AgentRegistry registry)
    {
        var component = args["component"]?.GetValue<string>() ?? "";

        // Route to the appropriate agent based on component
        var message = $"Explain the architecture of {component}";
        var agent = registry.RouteToAgent(message);
        var response = await agent.ProcessMessageAsync(message, Guid.NewGuid().ToString("N"));

        return new
        {
            component,
            agentId = agent.AgentId,
            agentName = agent.Name,
            explanation = response
        };
    }

    private static Task<object> HandleCreatePrAsync(JsonObject args)
    {
        var repo = args["repo"]?.GetValue<string>() ?? "";
        var title = args["title"]?.GetValue<string>() ?? "Untitled PR";
        var description = args["description"]?.GetValue<string>() ?? "";

        var guidance = repo.ToLowerInvariant() switch
        {
            var r when r.Contains("fhir") => """
                ## PR Guidance for microsoft/fhir-server

                ### Branch
                ```
                feature/your-feature-name
                ```

                ### Steps
                1. Fork the repository
                2. Create a feature branch from `main`
                3. Implement your changes with tests
                4. Run `dotnet test` locally
                5. Fill out the PR template
                6. Request review from `@microsoft/fhir-server-maintainers`

                ### Required Tests
                - Unit tests for new logic
                - Integration tests for data layer changes
                - Conformance tests for FHIR spec changes

                ### CI Matrix
                Windows + Linux, SQL Server + Cosmos DB
                """,
            var r when r.Contains("healthcare") || r.Contains("shared") => """
                ## PR Guidance for microsoft/healthcare-shared-components

                ### Branch
                ```
                feature/[package]-[description]
                ```

                ### Steps
                1. Update version in `.csproj` or `Directory.Build.props`
                2. Add XML doc comments for public APIs
                3. Write unit tests (>80% coverage)
                4. Verify downstream build (FHIR server)
                5. Update `CHANGELOG.md`
                6. Request 2 approvals from code owners

                ### Version Rules
                - Bug fix → patch
                - New API → minor
                - Breaking change → major + migration guide
                """,
            _ => """
                ## Generic PR Guidance

                1. Create a feature branch from `main`
                2. Write clean, documented code
                3. Add comprehensive tests
                4. Run the full test suite locally
                5. Fill out the PR description template
                6. Request review from area owners
                """
        };

        return Task.FromResult<object>(new
        {
            repo,
            title,
            description,
            guidance,
            previewUrl = $"https://github.com/microsoft/{repo}/compare/main...feature/pr-branch"
        });
    }

    private static Task<object> HandleListAgentsAsync(AgentRegistry registry)
    {
        var agents = registry.GetAllAgents().Select(a => new
        {
            a.AgentId,
            a.Name,
            skills = a.GetAgentCard().Skills.Select(s => new { s.Id, s.Name, s.Description }).ToList()
        }).ToList();

        return Task.FromResult<object>(new { agents, count = agents.Count });
    }

    private static async Task<object> HandleAskAgentAsync(JsonObject args, AgentRegistry registry)
    {
        var agentId = args["agentId"]?.GetValue<string>() ?? "";
        var message = args["message"]?.GetValue<string>() ?? "";

        var agent = registry.GetAgent(agentId);
        if (agent is null)
            return new { error = $"Agent '{agentId}' not found" };

        var response = await agent.ProcessMessageAsync(message, Guid.NewGuid().ToString("N"));

        return new
        {
            agentId = agent.AgentId,
            agentName = agent.Name,
            message,
            response
        };
    }
}
