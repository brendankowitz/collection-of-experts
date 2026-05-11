namespace AgentHost.Llm.Prompts;

public interface IPromptTemplate
{
    string AgentId { get; }

    string SkillId { get; }

    string Render(IReadOnlyDictionary<string, string> variables);
}
