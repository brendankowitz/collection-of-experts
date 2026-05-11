using AgentHost.A2A;
using AgentHost.Agents;
using AgentHost.Llm;
using AgentHost.Llm.Prompts;
using AgentHost.Repositories.Options;
using AgentHost.Repositories.Registry;
using AgentHost.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHost.Tests;

public sealed class DynamicAgentRegistryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task DynamicRegistry_LoadsAndUnloadsAgentsFromRepositoryChanges()
    {
        var repoRegistry = new InMemoryRepositoryRegistry();
        var initialRepo = await repoRegistry.CreateAsync(new Repository
        {
            Id = "custom-repo",
            OwnerOrOrg = "octo-org",
            Name = "custom-repo",
            CloneUrl = "https://github.com/octo-org/custom-repo.git",
            AgentPersona = "Custom Repo Expert",
            Enabled = true
        });

        var services = new ServiceCollection();
        services.AddSingleton<MockCodeIndexService>();
        services.AddSingleton<ITokenAccountant, TokenAccountant>();
        services.AddSingleton<IChatClientFactory>(sp =>
            new ChatClientFactory(
                Options.Create(new LlmOptions { DefaultProvider = "Mock", DefaultModel = "gpt-4o" }),
                sp.GetRequiredService<ITokenAccountant>(),
                NullLogger<ChatClientFactory>.Instance));
        services.AddSingleton(new PromptTemplateLibrary(NullLogger<PromptTemplateLibrary>.Instance));
        services.AddSingleton<ILogger<RepositoryExpertAgent>>(NullLogger<RepositoryExpertAgent>.Instance);

        using var provider = services.BuildServiceProvider();

        var agentRegistry = new AgentRegistry([], NullLogger<AgentRegistry>.Instance);
        var cardProvider = new AgentCardProvider();
        var dynamicRegistry = new DynamicAgentRegistry(
            repoRegistry,
            provider,
            agentRegistry,
            cardProvider,
            Options.Create(new RepositoriesOptions()),
            NullLogger<DynamicAgentRegistry>.Instance);

        await dynamicRegistry.StartAsync(CancellationToken.None);

        var agentId = RepositoryExpertAgent.BuildAgentId(initialRepo);
        agentRegistry.GetAgent(agentId).Should().NotBeNull();
        cardProvider.GetCard(agentId).Should().NotBeNull();

        initialRepo.Enabled = false;
        await repoRegistry.UpdateAsync(initialRepo);

        agentRegistry.GetAgent(agentId).Should().BeNull();
        cardProvider.GetCard(agentId).Should().BeNull();

        await dynamicRegistry.StopAsync(CancellationToken.None);
    }
}
