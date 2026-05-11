using AgentHost.Repositories.Registry;

namespace AgentHost.Repositories.Sources;

public sealed class RepositorySourceFactory
{
    private readonly GitHubRepositorySource _github;
    private readonly AzureDevOpsRepositorySource _azureDevOps;

    public RepositorySourceFactory(GitHubRepositorySource github, AzureDevOpsRepositorySource azureDevOps)
    {
        _github = github;
        _azureDevOps = azureDevOps;
    }

    public IRepositorySource GetSource(RepositorySource source) => source switch
    {
        RepositorySource.GitHub => _github,
        RepositorySource.AzureDevOps => _azureDevOps,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };
}
