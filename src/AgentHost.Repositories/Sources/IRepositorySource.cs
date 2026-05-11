using AgentHost.Repositories.Registry;

namespace AgentHost.Repositories.Sources;

public interface IRepositorySource
{
    Task<RepositoryMetadata> ProbeAsync(Repository repo, CancellationToken ct = default);
    Task<Stream> FetchTarballAsync(Repository repo, string @ref, CancellationToken ct = default);
}
