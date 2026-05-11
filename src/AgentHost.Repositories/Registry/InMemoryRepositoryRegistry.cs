using System.Collections.Concurrent;

namespace AgentHost.Repositories.Registry;

public sealed class InMemoryRepositoryRegistry : IRepositoryRegistry
{
    private readonly ConcurrentDictionary<string, Repository> _store = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<RepositoryChange>? Changed;

    public Task<Repository?> GetAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<Repository>> ListAsync(bool? enabled = null, CancellationToken ct = default)
    {
        IEnumerable<Repository> items = _store.Values;
        if (enabled.HasValue)
            items = items.Where(r => r.Enabled == enabled.Value);
        return Task.FromResult<IReadOnlyList<Repository>>(items.OrderBy(r => r.Name).ToList());
    }

    public Task<Repository> CreateAsync(Repository repository, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository.Id))
            repository.Id = Guid.NewGuid().ToString("N");
        repository.CreatedAtUtc = DateTime.UtcNow;
        repository.UpdatedAtUtc = DateTime.UtcNow;
        _store[repository.Id] = repository;
        Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Added, repository));
        return Task.FromResult(repository);
    }

    public Task<Repository> UpdateAsync(Repository repository, CancellationToken ct = default)
    {
        repository.UpdatedAtUtc = DateTime.UtcNow;
        _store[repository.Id] = repository;
        Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Updated, repository));
        return Task.FromResult(repository);
    }

    public Task DeleteAsync(string id, bool hard = false, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var repo))
        {
            if (hard)
            {
                _store.TryRemove(id, out _);
                Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Removed, repo));
            }
            else
            {
                repo.Enabled = false;
                repo.UpdatedAtUtc = DateTime.UtcNow;
                Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Updated, repo));
            }
        }
        return Task.CompletedTask;
    }

    public void Seed(IEnumerable<Repository> repositories)
    {
        foreach (var repo in repositories)
        {
            if (string.IsNullOrWhiteSpace(repo.Id))
                repo.Id = Guid.NewGuid().ToString("N");
            _store[repo.Id] = repo;
        }
    }
}
