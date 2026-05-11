using System.ComponentModel;
using System.Text.Json;
using AgentHost.A2A;
using AgentHost.Agents;
using AgentHost.Repositories.Registry;
using AgentHost.Repositories.Tasks;
using AgentHost.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace AgentHost.MCP;

[McpServerToolType]
public sealed class ExpertAgentsMcpTools(
    MockCodeIndexService codeIndex,
    AgentRegistry agentRegistry,
    IAgentTaskStore taskStore,
    IServiceProvider services,
    ILogger<ExpertAgentsMcpTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool(Name = "search_code")]
    [Description("Search indexed code for files matching a query.")]
    public string SearchCode(
        [Description("Repository identifier such as 'fhir-server' or 'healthcare-shared-components'.")] string repo,
        [Description("Free-text query to search for.")] string query,
        [Description("Maximum number of results to return. Defaults to 5.")] int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return Error("Parameter 'repo' is required.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("Parameter 'query' is required.");
        }

        var limit = topK <= 0 ? 5 : Math.Min(topK, 25);
        var results = codeIndex.Search(repo, query);
        var files = results
            .Take(limit)
            .Select(result => new
            {
                filePath = result.FilePath,
                snippet = result.Snippet.Length > 500 ? result.Snippet[..500] + "..." : result.Snippet
            })
            .ToList();

        return Serialize(new
        {
            repo,
            query,
            totalResults = results.Count,
            returned = files.Count,
            files
        });
    }

    [McpServerTool(Name = "get_file_content")]
    [Description("Retrieve the full content of a file from a repository.")]
    public string GetFileContent(
        [Description("Repository identifier.")] string repo,
        [Description("Path to the file within the repository.")] string filePath)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return Error("Parameter 'repo' is required.");
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Error("Parameter 'filePath' is required.");
        }

        var content = codeIndex.GetFileContent(repo, filePath);
        return Serialize(content is null
            ? new { repo, filePath, found = false, error = "File not found" }
            : new { repo, filePath, found = true, content });
    }

    [McpServerTool(Name = "explain_architecture")]
    [Description("Route an architecture question to the most relevant expert agent.")]
    public async Task<string> ExplainArchitecture(
        [Description("Component or subsystem to explain.")] string component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            return Error("Parameter 'component' is required.");
        }

        var prompt = $"Explain the architecture of {component}";
        var agent = agentRegistry.RouteToAgent(prompt);
        var sessionId = Guid.NewGuid().ToString("N");
        var response = await agent.ProcessMessageAsync(prompt, sessionId);

        return Serialize(new
        {
            component,
            threadId = sessionId,
            agentId = agent.AgentId,
            agentName = agent.Name,
            explanation = response
        });
    }

    [McpServerTool(Name = "create_pr")]
    [Description("Return repository-specific pull request guidance.")]
    public string CreatePr(
        [Description("Repository identifier.")] string repo,
        [Description("Proposed pull request title.")] string title = "Untitled PR",
        [Description("Proposed pull request description.")] string description = "")
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            return Error("Parameter 'repo' is required.");
        }

        var guidance = repo.ToLowerInvariant() switch
        {
            var value when value.Contains("fhir", StringComparison.Ordinal) => """
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
            var value when value.Contains("healthcare", StringComparison.Ordinal) || value.Contains("shared", StringComparison.Ordinal) => """
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

        return Serialize(new
        {
            repo,
            title,
            description,
            guidance,
            previewUrl = $"https://github.com/microsoft/{repo}/compare/main...feature/pr-branch"
        });
    }

    [McpServerTool(Name = "list_agents")]
    [Description("List all available expert agents.")]
    public string ListAgents()
    {
        var agents = agentRegistry.GetAllAgents()
            .Select(agent => new
            {
                agentId = agent.AgentId,
                name = agent.Name,
                skills = agent.GetAgentCard().Skills
                    .Select(skill => new { skill.Id, skill.Name, skill.Description })
                    .ToList()
            })
            .ToList();

        return Serialize(new { agents, count = agents.Count });
    }

    [McpServerTool(Name = "ask_agent")]
    [Description("Send a question to a specific expert agent.")]
    public async Task<string> AskAgent(
        [Description("Agent identifier, for example 'fhir-server-expert'.")] string agentId,
        [Description("Question or instruction for the agent.")] string message)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return Error("Parameter 'agentId' is required.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Error("Parameter 'message' is required.");
        }

        var agent = agentRegistry.GetAgent(agentId);
        if (agent is null)
        {
            return Error($"Agent '{agentId}' not found.");
        }

        var thread = await RunAgentConversationAsync(agent, message);
        return Serialize(new
        {
            threadId = thread.ThreadId,
            agentId = agent.AgentId,
            agentName = agent.Name,
            message,
            response = thread.Response
        });
    }

    [McpServerTool(Name = "list_repositories")]
    [Description("List repositories managed by the platform.")]
    public async Task<string> ListRepositories()
    {
        var registry = services.GetService<IRepositoryRegistry>();
        if (registry is null)
        {
            return Serialize(new { repositories = Array.Empty<object>(), count = 0, source = "empty" });
        }

        var repos = await registry.ListAsync(enabled: true);
        var repositories = repos.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            url = r.CloneUrl,
            language = r.LanguageHints.Length > 0 ? r.LanguageHints[0] : null,
            agentPersona = r.AgentPersona
        }).ToList();

        return Serialize(new { repositories, count = repositories.Count, source = "registry" });
    }

    [McpServerTool(Name = "ask_repo_expert")]
    [Description("Ask the expert agent that owns a repository.")]
    public async Task<string> AskRepoExpert(
        [Description("Repository identifier.")] string repoId,
        [Description("Question for the repository expert.")] string question,
        [Description("Optional prior thread identifier to continue.")] string? threadId = null)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            return Error("Parameter 'repoId' is required.");
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return Error("Parameter 'question' is required.");
        }

        var agent = FindAgentForRepository(repoId, question);
        var thread = await RunAgentConversationAsync(agent, question, threadId);

        return Serialize(new
        {
            repoId,
            threadId = thread.ThreadId,
            createdNewThread = thread.CreatedNewThread,
            agentId = agent.AgentId,
            agentName = agent.Name,
            question,
            response = thread.Response
        });
    }

    [McpServerTool(Name = "submit_followup")]
    [Description("Submit a follow-up message to an existing MCP conversation thread.")]
    public async Task<string> SubmitFollowup(
        [Description("Thread identifier returned by ask_agent or ask_repo_expert.")] string threadId,
        [Description("Follow-up message to send.")] string message)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return Error("Parameter 'threadId' is required.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Error("Parameter 'message' is required.");
        }

        var existingTask = await taskStore.GetTaskAsync(threadId);
        var agent = existingTask is not null
            ? agentRegistry.GetAgent(existingTask.AgentId ?? string.Empty)
            : null;

        agent ??= agentRegistry.RouteToAgent(message);
        var thread = await RunAgentConversationAsync(agent, message, existingTask?.Id ?? threadId);

        return Serialize(new
        {
            threadId = thread.ThreadId,
            existingThread = existingTask is not null,
            createdNewThread = thread.CreatedNewThread,
            agentId = agent.AgentId,
            agentName = agent.Name,
            message,
            response = thread.Response
        });
    }

    private static string Serialize(object result)
        => JsonSerializer.Serialize(result, JsonOptions);

    private static string Error(string message)
        => Serialize(new { error = message });

    private IExpertAgent FindAgentForRepository(string repoId, string question)
    {
        var directMatch = agentRegistry.GetAllAgents()
            .FirstOrDefault(agent => agent.AgentId.Contains(repoId, StringComparison.OrdinalIgnoreCase));
        if (directMatch is not null)
        {
            return directMatch;
        }

        if (repoId.Contains("healthcare", StringComparison.OrdinalIgnoreCase) ||
            repoId.Contains("shared", StringComparison.OrdinalIgnoreCase))
        {
            var healthcareAgent = agentRegistry.GetAgent("healthcare-components-expert");
            if (healthcareAgent is not null)
            {
                return healthcareAgent;
            }
        }

        if (repoId.Contains("fhir", StringComparison.OrdinalIgnoreCase))
        {
            var fhirAgent = agentRegistry.GetAgent("fhir-server-expert");
            if (fhirAgent is not null)
            {
                return fhirAgent;
            }
        }

        return agentRegistry.RouteToAgent(question);
    }

    private async Task<ConversationResult> RunAgentConversationAsync(IExpertAgent agent, string message, string? threadId = null)
    {
        var record = string.IsNullOrWhiteSpace(threadId) ? null : await taskStore.GetTaskAsync(threadId);
        var createdNewThread = record is null;
        var sessionId = record?.SessionId ?? Guid.NewGuid().ToString("N");

        if (record is null)
        {
            record = await taskStore.CreateTaskAsync(sessionId, agent.AgentId, "user", message);
        }
        else
        {
            if (!string.Equals(record.AgentId, agent.AgentId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Thread {ThreadId} was previously associated with {ExistingAgentId} but is being handled by {AgentId}.",
                    record.Id,
                    record.AgentId,
                    agent.AgentId);
            }

            await taskStore.AppendMessageAsync(record.Id, "user", message);
            await taskStore.UpdateTaskAsync(record.Id, TaskState.Working);
        }

        await taskStore.UpdateTaskAsync(record.Id, TaskState.Working);
        var response = await agent.ProcessMessageAsync(message, sessionId);
        await taskStore.CompleteTaskAsync(record.Id, "agent", response);

        return new ConversationResult(record.Id, createdNewThread, response);
    }

    private sealed record ConversationResult(string ThreadId, bool CreatedNewThread, string Response);
}
