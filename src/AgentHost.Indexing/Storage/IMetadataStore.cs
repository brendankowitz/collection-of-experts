namespace AgentHost.Indexing.Storage;

/// <summary>
/// Abstraction over the relational metadata store (Postgres).
/// </summary>
public interface IMetadataStore
{
    // ── Repositories ──────────────────────────────────────────────────────────

    Task<RepositoryRecord?> GetRepositoryAsync(string repoId, CancellationToken ct = default);
    Task<IReadOnlyList<RepositoryRecord>> ListRepositoriesAsync(CancellationToken ct = default);
    Task UpsertRepositoryAsync(RepositoryRecord repo, CancellationToken ct = default);

    // ── Indexed files ─────────────────────────────────────────────────────────

    Task<IndexedFileRecord?> GetIndexedFileAsync(string repoId, string filePath, CancellationToken ct = default);
    Task UpsertIndexedFileAsync(IndexedFileRecord file, CancellationToken ct = default);

    // ── Code chunks ───────────────────────────────────────────────────────────

    Task UpsertChunkAsync(CodeChunkRecord chunk, CancellationToken ct = default);
    Task<IReadOnlyList<CodeChunkRecord>> GetChunksByRepoAsync(string repoId, CancellationToken ct = default);
    Task<IReadOnlyList<CodeChunkRecord>> GetChunksByVectorIdsAsync(IEnumerable<Guid> vectorIds, CancellationToken ct = default);
    Task DeleteChunksByFileAsync(string repoId, string filePath, CancellationToken ct = default);

    // ── Indexing jobs ─────────────────────────────────────────────────────────

    Task<IndexingJobRecord?> GetNextPendingJobAsync(CancellationToken ct = default);
    Task<IndexingJobRecord?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task CreateJobAsync(IndexingJobRecord job, CancellationToken ct = default);
    Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? error = null, CancellationToken ct = default);

    // ── Migration ─────────────────────────────────────────────────────────────

    Task MigrateAsync(CancellationToken ct = default);
}

// ── Record types ─────────────────────────────────────────────────────────────

/// <summary>A tracked repository.</summary>
public sealed record RepositoryRecord(
    string Id, string Name, string Url, string DefaultBranch,
    string? LanguageHints, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>A file whose chunks are indexed.</summary>
public sealed record IndexedFileRecord(
    Guid Id, string RepoId, string FilePath, string ContentHash, DateTime IndexedAt);

/// <summary>A single code chunk stored in both Postgres and Qdrant.</summary>
public sealed record CodeChunkRecord(
    Guid Id, string RepoId, string FilePath, string Language,
    int StartLine, int EndLine, string? SymbolName, string ChunkKind,
    string ContentHash, DateTime? EmbeddedAt, Guid? VectorId);

/// <summary>An indexing job.</summary>
public sealed record IndexingJobRecord(
    Guid Id, string RepoId, JobKind Kind, JobStatus Status,
    DateTime StartedAt, DateTime? FinishedAt, string? Error);

/// <summary>Full vs. incremental indexing.</summary>
public enum JobKind { Full, Incremental }

/// <summary>Job lifecycle state.</summary>
public enum JobStatus { Pending, Running, Completed, Failed }
