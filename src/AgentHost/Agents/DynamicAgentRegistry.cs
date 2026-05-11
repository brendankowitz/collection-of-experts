using System.Collections.Concurrent;
using AgentHost.A2A;
using AgentHost.Repositories.Options;
using AgentHost.Repositories.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AgentHost.Agents;

public sealed class DynamicAgentRegistry : IHostedService
{
    private readonly ConcurrentDictionary<string, IExpertAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRepositoryRegistry _repoRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentCardProvider _cardProvider;
    private readonly RepositoriesOptions _options;
    private readonly ILogger<DynamicAgentRegistry> _logger;

    public DynamicAgentRegistry(
        IRepositoryRegistry repoRegistry,
        IServiceProvider serviceProvider,
        AgentRegistry agentRegistry,
        AgentCardProvider cardProvider,
        IOptions<RepositoriesOptions> options,
        ILogger<DynamicAgentRegistry> logger)
    {
        _repoRegistry = repoRegistry;
        _serviceProvider = serviceProvider;
        _agentRegistry = agentRegistry;
        _cardProvider = cardProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_repoRegistry is PostgresRepositoryRegistry postgres)
            await postgres.MigrateAsync(ct);

        var existing = await _repoRegistry.ListAsync(ct: ct);
        if (existing.Count == 0)
            await SeedDefaultRepositoriesAsync(ct);

        var repos = await _repoRegistry.ListAsync(enabled: true, ct: ct);
        foreach (var repo in repos)
            RegisterAgentForRepo(repo);

        _repoRegistry.Changed += OnRegistryChanged;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _repoRegistry.Changed -= OnRegistryChanged;
        return Task.CompletedTask;
    }

    private void OnRegistryChanged(object? sender, RepositoryChange change)
    {
        switch (change.Kind)
        {
            case RepositoryChangeKind.Added:
                if (change.Repository.Enabled)
                    RegisterAgentForRepo(change.Repository);
                break;
            case RepositoryChangeKind.Updated:
                UnregisterAgentForRepo(change.Repository);
                if (change.Repository.Enabled)
                    RegisterAgentForRepo(change.Repository);
                break;
            case RepositoryChangeKind.Removed:
                UnregisterAgentForRepo(change.Repository);
                break;
        }
    }

    private void RegisterAgentForRepo(Repository repo)
    {
        var agent = ActivatorUtilities.CreateInstance<RepositoryExpertAgent>(_serviceProvider, repo);
        _agents[agent.AgentId] = agent;
        _agentRegistry.RegisterAgent(agent);
        _cardProvider.RegisterCard(agent.GetAgentCard());
        _logger.LogInformation("Registered dynamic agent {AgentId} for repo {RepoName}", agent.AgentId, repo.Name);
    }

    private void UnregisterAgentForRepo(Repository repo)
    {
        var agentId = RepositoryExpertAgent.BuildAgentId(repo);
        _agents.TryRemove(agentId, out _);
        _agentRegistry.UnregisterAgent(agentId);
        _cardProvider.UnregisterCard(agentId);
        _logger.LogInformation("Unregistered dynamic agent {AgentId}", agentId);
    }

    private async Task SeedDefaultRepositoriesAsync(CancellationToken ct)
    {
        var seeds = _options.Seed.Length > 0
            ? _options.Seed.Select(seed => new Repository
            {
                Id = seed.Name.Equals("fhir-server", StringComparison.OrdinalIgnoreCase)
                    ? "fhir-server"
                    : seed.Name.Equals("healthcare-shared-components", StringComparison.OrdinalIgnoreCase)
                        ? "healthcare-components"
                        : Guid.NewGuid().ToString("N"),
                Source = seed.Source.Equals("azure_devops", StringComparison.OrdinalIgnoreCase) || seed.Source.Equals("azuredevops", StringComparison.OrdinalIgnoreCase)
                    ? RepositorySource.AzureDevOps
                    : RepositorySource.GitHub,
                OwnerOrOrg = seed.OwnerOrOrg,
                Name = seed.Name,
                DefaultBranch = "main",
                CloneUrl = seed.Source.Equals("azure_devops", StringComparison.OrdinalIgnoreCase) || seed.Source.Equals("azuredevops", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $"https://github.com/{seed.OwnerOrOrg}/{seed.Name}.git",
                AgentPersona = string.IsNullOrWhiteSpace(seed.AgentPersona) ? $"{seed.Name} Expert" : seed.AgentPersona,
                LanguageHints = ["csharp"],
                Enabled = seed.Enabled
            }).ToArray()
            :
            [
                new Repository
                {
                    Id = "fhir-server",
                    Source = RepositorySource.GitHub,
                    OwnerOrOrg = "microsoft",
                    Name = "fhir-server",
                    DefaultBranch = "main",
                    CloneUrl = "https://github.com/microsoft/fhir-server.git",
                    AgentPersona = "FHIR Server Expert",
                    LanguageHints = ["csharp"],
                    Enabled = true
                },
                new Repository
                {
                    Id = "healthcare-components",
                    Source = RepositorySource.GitHub,
                    OwnerOrOrg = "microsoft",
                    Name = "healthcare-shared-components",
                    DefaultBranch = "main",
                    CloneUrl = "https://github.com/microsoft/healthcare-shared-components.git",
                    AgentPersona = "Healthcare Shared Components Expert",
                    LanguageHints = ["csharp"],
                    Enabled = true
                }
            ];

        foreach (var seed in seeds)
        {
            await _repoRegistry.CreateAsync(seed, ct);
            _logger.LogInformation("Seeded repository {RepoId}", seed.Id);
        }
    }
}
