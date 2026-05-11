using AgentHost.Llm;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHost.Tests;

public sealed class ChatClientFactoryTests
{
    [Fact]
    public async Task CreateForAgent_UsesAgentOverrideAndTracksUsage()
    {
        var options = Options.Create(new LlmOptions
        {
            DefaultProvider = "Mock",
            DefaultModel = "default-model",
            AgentOverrides =
            {
                ["fhir-server-expert"] = new AgentOverrideOptions
                {
                    Provider = "Mock",
                    Model = "override-model"
                }
            }
        });

        using var accountant = new TokenAccountant();
        var factory = new ChatClientFactory(options, accountant, NullLogger<ChatClientFactory>.Instance);

        using var client = factory.CreateForAgent("fhir-server-expert");
        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello world")]);

        response.ModelId.Should().Be("override-model");
        accountant.GetHistory().Should().ContainSingle();
        accountant.GetHistory()[0].Model.Should().Be("override-model");
    }
}
