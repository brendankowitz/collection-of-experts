using Microsoft.Extensions.AI;

namespace AgentHost.Llm;

public interface IChatClientFactory
{
    IChatClient CreateForAgent(string agentId);

    IChatClient CreateDefault();
}
