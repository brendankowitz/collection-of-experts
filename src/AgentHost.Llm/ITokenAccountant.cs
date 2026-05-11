namespace AgentHost.Llm;

public interface ITokenAccountant
{
    void Record(TokenUsage usage);

    IReadOnlyList<TokenUsage> GetHistory();
}
