using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentHost.Tests;

public sealed class LlmOptionsTests
{
    [Fact]
    public void Configuration_BindsLlmOptionsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:DefaultProvider"] = "Mock",
                ["Llm:DefaultModel"] = "gpt-4o-mini",
                ["Llm:Providers:Ollama:Endpoint"] = "http://localhost:11434",
                ["Llm:AgentOverrides:fhir-server-expert:Model"] = "claude-3-5-sonnet"
            })
            .Build();

        var options = new AgentHost.Llm.LlmOptions();
        configuration.GetSection(AgentHost.Llm.LlmOptions.SectionName).Bind(options);

        options.DefaultProvider.Should().Be("Mock");
        options.DefaultModel.Should().Be("gpt-4o-mini");
        options.Providers["Ollama"].Endpoint.Should().Be("http://localhost:11434");
        options.AgentOverrides["fhir-server-expert"].Model.Should().Be("claude-3-5-sonnet");
    }
}
