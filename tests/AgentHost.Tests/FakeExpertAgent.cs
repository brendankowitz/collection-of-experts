using System.Runtime.CompilerServices;
using AgentHost.A2A;
using AgentHost.Agents;

namespace AgentHost.Tests;

/// <summary>
/// Simple in-memory <see cref="IExpertAgent"/> for use in unit tests.
/// Counts invocations and returns a fixed response string.
/// </summary>
internal sealed class FakeExpertAgent(string agentId, string name, string fixedResponse) : IExpertAgent
{
    public string AgentId => agentId;
    public string Name => name;

    /// <summary>How many times <see cref="ProcessMessageAsync"/> was called.</summary>
    public int CallCount { get; private set; }

    public AgentCard GetAgentCard() => new()
    {
        AgentId = agentId,
        Name = name,
        Description = $"Fake agent {agentId}",
        Version = "1.0.0",
        Url = $"inproc://{agentId}",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills = []
    };

    public Task<string> ProcessMessageAsync(string message, string sessionId)
    {
        CallCount++;
        return Task.FromResult(fixedResponse);
    }

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(
        string message, string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        CallCount++;
        // Yield words from the response to simulate streaming
        foreach (var word in fixedResponse.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            yield return word + " ";
            await Task.Delay(1, ct);
        }
    }
}
