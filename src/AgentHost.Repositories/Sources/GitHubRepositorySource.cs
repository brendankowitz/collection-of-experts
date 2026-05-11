using AgentHost.Repositories.Options;
using AgentHost.Repositories.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace AgentHost.Repositories.Sources;

public sealed class GitHubRepositorySource : IRepositorySource
{
    private readonly GitHubSourceOptions _options;
    private readonly ILogger<GitHubRepositorySource> _logger;

    public GitHubRepositorySource(IOptions<RepositoriesOptions> options, ILogger<GitHubRepositorySource> logger)
    {
        _options = options.Value.Sources.GitHub;
        _logger = logger;
    }

    private GitHubClient CreateClient()
    {
        var client = new GitHubClient(new ProductHeaderValue("ExpertAgentsHost"));
        if (!string.IsNullOrEmpty(_options.Pat))
            client.Credentials = new Credentials(_options.Pat);
        return client;
    }

    public async Task<RepositoryMetadata> ProbeAsync(Registry.Repository repo, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            var ghRepo = await client.Repository.Get(repo.OwnerOrOrg, repo.Name);
            return new RepositoryMetadata(
                ghRepo.FullName,
                ghRepo.DefaultBranch,
                ghRepo.CloneUrl,
                ghRepo.Description ?? string.Empty,
                ghRepo.Private);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe GitHub repo {Owner}/{Name}", repo.OwnerOrOrg, repo.Name);
            return new RepositoryMetadata(
                $"{repo.OwnerOrOrg}/{repo.Name}",
                repo.DefaultBranch,
                repo.CloneUrl,
                string.Empty,
                false);
        }
    }

    public async Task<Stream> FetchTarballAsync(Registry.Repository repo, string @ref, CancellationToken ct = default)
    {
        var client = CreateClient();
        var bytes = await client.Repository.Content.GetArchive(repo.OwnerOrOrg, repo.Name, ArchiveFormat.Tarball, @ref);
        return new MemoryStream(bytes);
    }
}
