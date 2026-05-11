using System.Runtime.CompilerServices;
using AgentHost.A2A;
using AgentHost.Llm;
using AgentHost.Llm.Prompts;
using AgentHost.Services;
using Microsoft.Extensions.AI;

namespace AgentHost.Agents;

public sealed class HealthcareComponentsAgent : IExpertAgent
{
    private const string RepositoryId = "healthcare-shared-components";
    private readonly MockCodeIndexService _codeIndex;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly PromptTemplateLibrary _promptLibrary;
    private readonly ILogger<HealthcareComponentsAgent> _logger;

    public HealthcareComponentsAgent(
        MockCodeIndexService codeIndex,
        IChatClientFactory chatClientFactory,
        PromptTemplateLibrary promptLibrary,
        ILogger<HealthcareComponentsAgent> logger)
    {
        _codeIndex = codeIndex;
        _chatClientFactory = chatClientFactory;
        _promptLibrary = promptLibrary;
        _logger = logger;
    }

    public string AgentId => "healthcare-components-expert";

    public string Name => "Healthcare Shared Components Expert";

    public AgentCard GetAgentCard() => new()
    {
        AgentId = AgentId,
        Name = Name,
        Description = "Expert agent specialised in Microsoft Healthcare Shared Components. Covers SQL connection management, blob storage, exception handling, configuration, Mediator, and health checks.",
        Version = "1.0.0",
        Url = "http://localhost:5002",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "code-search",
                Name = "Shared Components Code Search",
                Description = "Search the microsoft/healthcare-shared-components repository.",
                ExampleQueries = ["Find RetrySqlConnectionWrapper", "Show me the blob client"]
            },
            new AgentSkill
            {
                Id = "architecture-qa",
                Name = "Shared Components Architecture Q&A",
                Description = "Explain how shared components fit together and are consumed by downstream services.",
                ExampleQueries = ["How does the retry wrapper work?", "Explain the Mediator pattern usage"]
            },
            new AgentSkill
            {
                Id = "pr-guidance",
                Name = "PR Guidance",
                Description = "Step-by-step guidance for creating pull requests.",
                ExampleQueries = ["How do I version a new shared package?"]
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
        new ChatMessage(ChatRole.System, "You are the Healthcare Shared Components Expert for the microsoft/healthcare-shared-components repository. Give precise, architecture-aware answers grounded in the supplied context."),
        new ChatMessage(ChatRole.User, prompt)
    ];
}
