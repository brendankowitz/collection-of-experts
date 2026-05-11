using System.Runtime.CompilerServices;
using AgentHost.A2A;
using AgentHost.Llm;
using AgentHost.Llm.Prompts;
using AgentHost.Repositories.Registry;
using AgentHost.Services;
using Microsoft.Extensions.AI;

namespace AgentHost.Agents;

public sealed class RepositoryExpertAgent : IExpertAgent
{
    private readonly Repository _repo;
    private readonly MockCodeIndexService _codeIndex;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly PromptTemplateLibrary _promptLibrary;
    private readonly ILogger<RepositoryExpertAgent> _logger;

    public RepositoryExpertAgent(
        Repository repo,
        MockCodeIndexService codeIndex,
        IChatClientFactory chatClientFactory,
        PromptTemplateLibrary promptLibrary,
        ILogger<RepositoryExpertAgent> logger)
    {
        _repo = repo;
        _codeIndex = codeIndex;
        _chatClientFactory = chatClientFactory;
        _promptLibrary = promptLibrary;
        _logger = logger;
    }

    public string AgentId => BuildAgentId(_repo);

    public string Name => _repo.AgentPersona.Length > 0 ? _repo.AgentPersona : $"{_repo.Name} Expert";

    public AgentCard GetAgentCard() => new()
    {
        AgentId = AgentId,
        Name = Name,
        Description = $"Expert agent for the {_repo.OwnerOrOrg}/{_repo.Name} repository. Answers architecture questions, searches code, and guides PRs.",
        Version = "1.0.0",
        Url = "http://localhost:5000",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "code-search",
                Name = $"{_repo.Name} Code Search",
                Description = $"Search the {_repo.OwnerOrOrg}/{_repo.Name} repository.",
                ExampleQueries = [$"Find the main entry point in {_repo.Name}", $"Show me authentication in {_repo.Name}"]
            },
            new AgentSkill
            {
                Id = "architecture-qa",
                Name = "Architecture Q&A",
                Description = "Explain architectural decisions, data flow, and component interactions.",
                ExampleQueries = ["How does the service work?", "Explain the data layer"]
            },
            new AgentSkill
            {
                Id = "pr-guidance",
                Name = "PR Guidance",
                Description = "Step-by-step guidance for creating pull requests.",
                ExampleQueries = ["How do I submit a PR?", "Branch naming conventions"]
            }
        ]
    };

    public async Task<string> ProcessMessageAsync(string message, string sessionId)
    {
        _logger.LogInformation("[{AgentId}] Processing message in session {SessionId}", AgentId, sessionId);
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
                yield return update.Text;
        }
    }

    public static string BuildAgentId(Repository repo)
    {
        if (repo.OwnerOrOrg.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
            repo.Name.Equals("fhir-server", StringComparison.OrdinalIgnoreCase))
            return "fhir-server-expert";

        if (repo.OwnerOrOrg.Equals("microsoft", StringComparison.OrdinalIgnoreCase) &&
            repo.Name.Equals("healthcare-shared-components", StringComparison.OrdinalIgnoreCase))
            return "healthcare-components-expert";

        return $"{repo.OwnerOrOrg}-{repo.Name}-expert"
            .ToLowerInvariant()
            .Replace("/", "-")
            .Replace(" ", "-");
    }

    private static string DetectSkill(string message)
    {
        var lowered = message.ToLowerInvariant();
        if (lowered.Contains("architecture") || lowered.Contains("how does") || lowered.Contains("explain"))
            return "architecture-qa";
        if (lowered.Contains("pr") || lowered.Contains("pull request") || lowered.Contains("contribute"))
            return "pr-guidance";
        return "code-search";
    }

    private string BuildPrompt(string message, string skillId)
    {
        var results = _codeIndex.Search(_repo.Name, message);
        var context = results.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, results.Take(3).Select(static r => $"File: {r.FilePath}{Environment.NewLine}{r.Snippet}"))
            : "No specific code snippets found.";

        if (_repo.PromptOverrides.TryGetValue(skillId, out var overrideTemplate))
        {
            return overrideTemplate
                .Replace("{{user_query}}", message)
                .Replace("{{repo_context}}", context);
        }

        try
        {
            return _promptLibrary.Render(AgentId, skillId, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["user_query"] = message,
                ["repo_context"] = context,
                ["conversation"] = string.Empty
            });
        }
        catch
        {
            return $"Repository: {_repo.OwnerOrOrg}/{_repo.Name}{Environment.NewLine}{Environment.NewLine}Context:{Environment.NewLine}{context}{Environment.NewLine}{Environment.NewLine}Question: {message}";
        }
    }

    private ChatMessage[] BuildMessages(string prompt) =>
    [
        new ChatMessage(ChatRole.System, $"You are the {Name} for the {_repo.OwnerOrOrg}/{_repo.Name} repository. Give precise, code-aware answers grounded in the provided context."),
        new ChatMessage(ChatRole.User, prompt)
    ];
}
