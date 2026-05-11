using System.Collections.Concurrent;

namespace AgentHost.Indexing.Storage;

/// <summary>
/// Thread-safe in-memory <see cref="IMetadataStore"/> for unit tests and dev.
/// </summary>
public sealed class InMemoryMetadataStore : IMetadataStore
{
    private readonly ConcurrentDictionary<string, RepositoryRecord> _repos = new();
    private readonly ConcurrentDictionary<string, IndexedFileRecord> _files = new();
    private readonly ConcurrentDictionary<Guid, CodeChunkRecord> _chunks = new();
    private readonly ConcurrentDictionary<Guid, IndexingJobRecord> _jobs = new();

    /// <inheritdoc />
    public Task MigrateAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<RepositoryRecord?> GetRepositoryAsync(string repoId, CancellationToken ct = default)
        => Task.FromResult(_repos.TryGetValue(repoId, out var r) ? r : null);

    /// <inheritdoc />
    public Task<IReadOnlyList<RepositoryRecord>> ListRepositoriesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RepositoryRecord>>(_repos.Values.ToList());

    /// <inheritdoc />
    public Task UpsertRepositoryAsync(RepositoryRecord repo, CancellationToken ct = default)
    {
        _repos[repo.Id] = repo;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IndexedFileRecord?> GetIndexedFileAsync(string repoId, string filePath, CancellationToken ct = default)
    {
        var key = $"{repoId}:{filePath}";
        return Task.FromResult(_files.TryGetValue(key, out var f) ? f : null);
    }

    /// <inheritdoc />
    public Task UpsertIndexedFileAsync(IndexedFileRecord file, CancellationToken ct = default)
    {
        _files[$"{file.RepoId}:{file.FilePath}"] = file;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertChunkAsync(CodeChunkRecord chunk, CancellationToken ct = default)
    {
        _chunks[chunk.Id] = chunk;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CodeChunkRecord>> GetChunksByRepoAsync(string repoId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CodeChunkRecord>>(
            _chunks.Values.Where(c => c.RepoId == repoId).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<CodeChunkRecord>> GetChunksByVectorIdsAsync(IEnumerable<Guid> vectorIds, CancellationToken ct = default)
    {
        var set = vectorIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<CodeChunkRecord>>(
            _chunks.Values.Where(c => c.VectorId.HasValue && set.Contains(c.VectorId.Value)).ToList());
    }

    /// <inheritdoc />
    public Task DeleteChunksByFileAsync(string repoId, string filePath, CancellationToken ct = default)
    {
        foreach (var key in _chunks.Keys
            .Where(k => _chunks[k].RepoId == repoId && _chunks[k].FilePath == filePath)
            .ToList())
        {
            _chunks.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IndexingJobRecord?> GetNextPendingJobAsync(CancellationToken ct = default)
    {
        var job = _jobs.Values
            .Where(j => j.Status == JobStatus.Pending)
            .OrderBy(j => j.StartedAt)
            .FirstOrDefault();
        return Task.FromResult(job);
    }

    /// <inheritdoc />
    public Task<IndexingJobRecord?> GetJobAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(_jobs.TryGetValue(jobId, out var j) ? j : null);

    /// <inheritdoc />
    public Task CreateJobAsync(IndexingJobRecord job, CancellationToken ct = default)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? error = null, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            _jobs[jobId] = job with
            {
                Status = status,
                FinishedAt = status is JobStatus.Completed or JobStatus.Failed ? DateTime.UtcNow : job.FinishedAt,
                Error = error ?? job.Error
            };
        }
        return Task.CompletedTask;
    }
}
