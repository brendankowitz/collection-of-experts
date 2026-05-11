namespace AgentHost.Repositories.Registry;

public interface IRepositoryRegistry
{
    Task<Repository?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Repository>> ListAsync(bool? enabled = null, CancellationToken ct = default);
    Task<Repository> CreateAsync(Repository repository, CancellationToken ct = default);
    Task<Repository> UpdateAsync(Repository repository, CancellationToken ct = default);
    Task DeleteAsync(string id, bool hard = false, CancellationToken ct = default);
    event EventHandler<RepositoryChange> Changed;
}
