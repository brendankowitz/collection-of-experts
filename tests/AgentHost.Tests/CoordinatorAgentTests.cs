using AgentHost.A2A;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using AgentHost.Orchestration;
using AgentHost.Agents;
using System.Runtime.CompilerServices;
using AgentHost.Llm;
using Microsoft.Extensions.AI;

namespace AgentHost.Tests;

/// <summary>
/// Tests for the CoordinatorAgent (test 4):
/// decomposes a question into ≥2 sub-queries and calls both agents.
/// </summary>
public sealed class CoordinatorAgentTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CoordinatorAgent_DecomposesInto2SubQueries_CallsBothAgents()
    {
        // Arrange
        // Two fake agents
        var agent1 = new FakeExpertAgent("fhir-server-expert", "FHIR Expert", "fhir answer");
        var agent2 = new FakeExpertAgent("healthcare-components-expert", "HC Expert", "hc answer");

        var registry = new AgentRegistry([agent1, agent2], NullLogger<AgentRegistry>.Instance);

        // Fake chat client that returns a predetermined decomposition JSON
        const string decompositionJson = @"[
            {""agent_id"":""fhir-server-expert"",""sub_query"":""FHIR search params"",""reason"":""FHIR domain""},
            {""agent_id"":""healthcare-components-expert"",""sub_query"":""Retry patterns"",""reason"":""HC domain""}
        ]";

        var chatFactory = new FakeChatClientFactory(decompositionJson);

        var options = Options.Create(new OrchestrationOptions
        {
            Coordinator = new CoordinatorOptions
            {
                MaxParallelAgents = 4,
                EnableSynthesis = false  // off for determinism
            }
        });

        var inProcessClient = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), options, NullLogger<InProcessA2AClient>.Instance);
        var a2aClient = new CompositeA2AClient(
            inProcessClient,
            null!,  // HTTP client unused (all calls are inproc)
            options);

        var coordinator = new CoordinatorAgent(chatFactory, new Lazy<AgentRegistry>(() => registry), a2aClient, options, NullLogger<CoordinatorAgent>.Instance);

        // Act
        var response = await coordinator.ProcessMessageAsync("How do FHIR and HC components interact?", "sess1");

        // Assert: both agents were called
        agent1.CallCount.Should().Be(1, "fhir-server-expert should have been called once");
        agent2.CallCount.Should().Be(1, "healthcare-components-expert should have been called once");

        response.Should().Contain("fhir answer");
        response.Should().Contain("hc answer");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CoordinatorAgent_StreamsTaggedResponses()
    {
        var agent1 = new FakeExpertAgent("fhir-server-expert", "FHIR Expert", "fhir answer");
        var agent2 = new FakeExpertAgent("healthcare-components-expert", "HC Expert", "hc answer");

        var registry = new AgentRegistry([agent1, agent2], NullLogger<AgentRegistry>.Instance);

        const string decompositionJson = @"[
            {""agent_id"":""fhir-server-expert"",""sub_query"":""FHIR q"",""reason"":""r""},
            {""agent_id"":""healthcare-components-expert"",""sub_query"":""HC q"",""reason"":""r""}
        ]";

        var options = Options.Create(new OrchestrationOptions
        {
            Coordinator = new CoordinatorOptions { EnableSynthesis = false }
        });

        var inProcessClient = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), options, NullLogger<InProcessA2AClient>.Instance);
        var a2aClient = new CompositeA2AClient(inProcessClient, null!, options);
        var coordinator = new CoordinatorAgent(new FakeChatClientFactory(decompositionJson), new Lazy<AgentRegistry>(() => registry), a2aClient, options, NullLogger<CoordinatorAgent>.Instance);

        var chunks = new List<string>();
        await foreach (var chunk in coordinator.ProcessMessageStreamAsync("multi-repo question", "s2", CancellationToken.None))
            chunks.Add(chunk);

        var full = string.Concat(chunks);
        full.Should().Contain("fhir-server-expert");
        full.Should().Contain("healthcare-components-expert");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeChatClientFactory(string fixedResponse) : IChatClientFactory
    {
        public IChatClient CreateForAgent(string agentId) => new FakeChatClient(fixedResponse);
        public IChatClient CreateDefault() => new FakeChatClient(fixedResponse);

        private sealed class FakeChatClient(string response) : IChatClient
        {
            public ChatClientMetadata Metadata { get; } = new("fake", null, "fake");

            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
                => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, response);
                await Task.CompletedTask;
            }

            public object? GetService(Type serviceType, object? serviceKey = null)
                => serviceType == typeof(ChatClientMetadata) ? Metadata : null;

            public void Dispose() { }
        }
    }
}
