namespace AgentHost.Llm.Prompts;

internal sealed class PromptTemplate : IPromptTemplate
{
    private readonly string _template;

    public PromptTemplate(string agentId, string skillId, string template)
    {
        AgentId = agentId;
        SkillId = skillId;
        _template = template;
    }

    public string AgentId { get; }

    public string SkillId { get; }

    public string Render(IReadOnlyDictionary<string, string> variables)
    {
        var result = _template;
        foreach (var (key, value) in variables)
        {
            result = result.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }

        return result;
    }
}
