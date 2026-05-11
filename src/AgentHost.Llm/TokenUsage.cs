namespace AgentHost.Llm;

public sealed record TokenUsage(
    string AgentId,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
