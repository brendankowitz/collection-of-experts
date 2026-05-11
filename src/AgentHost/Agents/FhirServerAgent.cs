using System.Runtime.CompilerServices;
using AgentHost.A2A;
using AgentHost.Llm;
using AgentHost.Llm.Prompts;
using AgentHost.Services;
using Microsoft.Extensions.AI;

namespace AgentHost.Agents;

public sealed class FhirServerAgent : IExpertAgent
{
    private const string RepositoryId = "fhir-server";
    private readonly MockCodeIndexService _codeIndex;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly PromptTemplateLibrary _promptLibrary;
    private readonly ILogger<FhirServerAgent> _logger;

    public FhirServerAgent(
        MockCodeIndexService codeIndex,
        IChatClientFactory chatClientFactory,
        PromptTemplateLibrary promptLibrary,
        ILogger<FhirServerAgent> logger)
    {
        _codeIndex = codeIndex;
        _chatClientFactory = chatClientFactory;
        _promptLibrary = promptLibrary;
        _logger = logger;
    }

    public string AgentId => "fhir-server-expert";

    public string Name => "FHIR Server Expert";

    public AgentCard GetAgentCard() => new()
    {
        AgentId = AgentId,
        Name = Name,
        Description = "Expert agent specialised in the Microsoft FHIR Server for Azure. Answers architecture questions, searches code, and guides PRs.",
        Version = "1.0.0",
        Url = "http://localhost:5001",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "code-search",
                Name = "FHIR Code Search",
                Description = "Search the microsoft/fhir-server repository.",
                ExampleQueries = ["Find the search parameter registry", "Show me $export implementation"]
            },
            new AgentSkill
            {
                Id = "architecture-qa",
                Name = "FHIR Architecture Q&A",
                Description = "Explain architectural decisions, data flow, and component interactions.",
                ExampleQueries = ["How does R4 search work?", "Explain the data layer abstraction"]
            },
            new AgentSkill
            {
                Id = "pr-guidance",
                Name = "PR Guidance",
                Description = "Step-by-step guidance for creating pull requests.",
                ExampleQueries = ["How do I submit a PR for a new search parameter?"]
            }
        ]
    };

    public async Task<string> ProcessMessageAsync(string message, string sessionId)
    {
        _logger.LogInformation("[{AgentId}] Processing message in session {SessionId}: {Message}", AgentId, sessionId, message);

        using var client = _chatClientFactory.CreateForAgent(AgentId);
        var prompt = BuildPrompt(message, DetectSkill(message));
        var response = await client.GetResponseAsync(BuildMessages(prompt), cancellationToken: CancellationToken.None).ConfigureAwait(false);
        return response.Text;
    }

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(
        string message,
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("[{AgentId}] Streaming message in session {SessionId}", AgentId, sessionId);

        using var client = _chatClientFactory.CreateForAgent(AgentId);
        var prompt = BuildPrompt(message, DetectSkill(message));

        await foreach (var update in client.GetStreamingResponseAsync(BuildMessages(prompt), cancellationToken: ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    private static string DetectSkill(string message)
    {
        var lowered = message.ToLowerInvariant();
        if (lowered.Contains("architecture", StringComparison.Ordinal) ||
            lowered.Contains("how does", StringComparison.Ordinal) ||
            lowered.Contains("explain", StringComparison.Ordinal) ||
            lowered.Contains("data flow", StringComparison.Ordinal))
        {
            return "architecture-qa";
        }

        if (lowered.Contains("pr", StringComparison.Ordinal) ||
            lowered.Contains("pull request", StringComparison.Ordinal) ||
            lowered.Contains("contribute", StringComparison.Ordinal) ||
            lowered.Contains("branch", StringComparison.Ordinal))
        {
            return "pr-guidance";
        }

        return "code-search";
    }

    private string BuildPrompt(string message, string skillId)
    {
        var results = _codeIndex.Search(RepositoryId, message);
        var context = results.Count > 0
            ? string.Join("\n\n", results.Take(3).Select(static result => $"File: {result.FilePath}\n{result.Snippet}"))
            : "No specific code snippets found.";

        return _promptLibrary.Render(AgentId, skillId, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user_query"] = message,
            ["repo_context"] = context,
            ["conversation"] = string.Empty
        });
    }

    private static ChatMessage[] BuildMessages(string prompt) =>
    [
        new ChatMessage(ChatRole.System, "You are the FHIR Server Expert for the microsoft/fhir-server repository. Give precise, code-aware answers grounded in the provided context."),
        new ChatMessage(ChatRole.User, prompt)
    ];
}
