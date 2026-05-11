using Microsoft.Extensions.Logging;

namespace AgentHost.Llm.Prompts;

public sealed class PromptTemplateLibrary
{
    private readonly Dictionary<string, IPromptTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PromptTemplateLibrary> _logger;

    public PromptTemplateLibrary(ILogger<PromptTemplateLibrary> logger)
    {
        _logger = logger;
    }

    public void LoadFromDirectory(string promptsBaseDirectory)
    {
        if (!Directory.Exists(promptsBaseDirectory))
        {
            _logger.LogWarning("Prompts directory not found: {Directory}", promptsBaseDirectory);
            return;
        }

        foreach (var agentDirectory in Directory.GetDirectories(promptsBaseDirectory))
        {
            var agentId = Path.GetFileName(agentDirectory);
            foreach (var filePath in Directory.GetFiles(agentDirectory, "*.md"))
            {
                var skillId = Path.GetFileNameWithoutExtension(filePath);
                var content = File.ReadAllText(filePath);
                RegisterTemplate(agentId, skillId, content);
                _logger.LogDebug("Loaded prompt template {AgentId}/{SkillId}", agentId, skillId);
            }
        }

        _logger.LogInformation("Loaded {Count} prompt templates from {Directory}", _templates.Count, promptsBaseDirectory);
    }

    public IPromptTemplate? TryGet(string agentId, string skillId)
        => _templates.TryGetValue(GetKey(agentId, skillId), out var template) ? template : null;

    public string Render(string agentId, string skillId, IReadOnlyDictionary<string, string> variables)
    {
        var template = TryGet(agentId, skillId);
        if (template is null)
        {
            _logger.LogWarning("Prompt template not found for {AgentId}/{SkillId}; falling back to user query", agentId, skillId);
            return variables.TryGetValue("user_query", out var userQuery) ? userQuery : string.Empty;
        }

        return template.Render(variables);
    }

    public void RegisterTemplate(string agentId, string skillId, string templateContent)
    {
        _templates[GetKey(agentId, skillId)] = new PromptTemplate(agentId, skillId, templateContent);
    }

    private static string GetKey(string agentId, string skillId) => $"{agentId}:{skillId}";
}
