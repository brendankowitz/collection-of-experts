using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using AgentHost.Indexing.Options;

namespace AgentHost.Indexing.Storage;

/// <summary>
/// Postgres-backed implementation of <see cref="IMetadataStore"/> using Npgsql + Dapper.
/// </summary>
public sealed class PostgresMetadataStore : IMetadataStore
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresMetadataStore> _logger;

    /// <summary>Initialises the store from options.</summary>
    public PostgresMetadataStore(IOptions<PostgresOptions> options, ILogger<PostgresMetadataStore> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
    }

    private NpgsqlConnection Open() => new(_connectionString);

    // ── Migration ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS repositories (
                id              TEXT PRIMARY KEY,
                name            TEXT NOT NULL,
                url             TEXT NOT NULL,
                default_branch  TEXT NOT NULL DEFAULT 'main',
                language_hints  TEXT,
                created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS indexed_files (
                id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                repo_id         TEXT NOT NULL REFERENCES repositories(id),
                file_path       TEXT NOT NULL,
                content_hash    TEXT NOT NULL,
                indexed_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(repo_id, file_path)
            );

            CREATE TABLE IF NOT EXISTS code_chunks (
                id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                repo_id         TEXT NOT NULL REFERENCES repositories(id),
                file_path       TEXT NOT NULL,
                language        TEXT NOT NULL,
                start_line      INT NOT NULL,
                end_line        INT NOT NULL,
                symbol_name     TEXT,
                chunk_kind      TEXT NOT NULL,
                content_hash    TEXT NOT NULL,
                embedded_at     TIMESTAMPTZ,
                vector_id       UUID,
                UNIQUE(repo_id, file_path, start_line, end_line)
            );

            CREATE INDEX IF NOT EXISTS idx_code_chunks_repo ON code_chunks(repo_id);
            CREATE INDEX IF NOT EXISTS idx_code_chunks_vector ON code_chunks(vector_id);

            CREATE TABLE IF NOT EXISTS indexing_jobs (
                id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                repo_id         TEXT NOT NULL REFERENCES repositories(id),
                kind            TEXT NOT NULL,
                status          TEXT NOT NULL DEFAULT 'pending',
                started_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                finished_at     TIMESTAMPTZ,
                error           TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_indexing_jobs_status ON indexing_jobs(status);
            """;

        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql);
        _logger.LogInformation("Postgres schema migration completed");
    }

    // ── Repositories ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<RepositoryRecord?> GetRepositoryAsync(string repoId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM repositories WHERE id = @Id", new { Id = repoId });
        return row is null ? null : MapRepo(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryRecord>> ListRepositoriesAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>("SELECT * FROM repositories ORDER BY name");
        return rows.Select(MapRepo).ToList();
    }

    /// <inheritdoc />
    public async Task UpsertRepositoryAsync(RepositoryRecord repo, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO repositories (id, name, url, default_branch, language_hints, created_at, updated_at)
            VALUES (@Id, @Name, @Url, @DefaultBranch, @LanguageHints, @CreatedAt, @UpdatedAt)
            ON CONFLICT (id) DO UPDATE
            SET name = EXCLUDED.name,
                url = EXCLUDED.url,
                default_branch = EXCLUDED.default_branch,
                language_hints = EXCLUDED.language_hints,
                updated_at = NOW()
            """,
            new { repo.Id, repo.Name, repo.Url, repo.DefaultBranch, repo.LanguageHints, repo.CreatedAt, repo.UpdatedAt });
    }

    // ── Indexed files ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IndexedFileRecord?> GetIndexedFileAsync(string repoId, string filePath, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM indexed_files WHERE repo_id = @RepoId AND file_path = @FilePath",
            new { RepoId = repoId, FilePath = filePath });
        return row is null ? null : MapIndexedFile(row);
    }

    /// <inheritdoc />
    public async Task UpsertIndexedFileAsync(IndexedFileRecord file, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO indexed_files (id, repo_id, file_path, content_hash, indexed_at)
            VALUES (@Id, @RepoId, @FilePath, @ContentHash, @IndexedAt)
            ON CONFLICT (repo_id, file_path) DO UPDATE
            SET content_hash = EXCLUDED.content_hash,
                indexed_at = NOW()
            """,
            new { file.Id, file.RepoId, file.FilePath, file.ContentHash, file.IndexedAt });
    }

    // ── Code chunks ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpsertChunkAsync(CodeChunkRecord chunk, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO code_chunks
                (id, repo_id, file_path, language, start_line, end_line, symbol_name, chunk_kind, content_hash, embedded_at, vector_id)
            VALUES
                (@Id, @RepoId, @FilePath, @Language, @StartLine, @EndLine, @SymbolName, @ChunkKind, @ContentHash, @EmbeddedAt, @VectorId)
            ON CONFLICT (repo_id, file_path, start_line, end_line) DO UPDATE
            SET symbol_name  = EXCLUDED.symbol_name,
                chunk_kind   = EXCLUDED.chunk_kind,
                content_hash = EXCLUDED.content_hash,
                embedded_at  = EXCLUDED.embedded_at,
                vector_id    = EXCLUDED.vector_id
            """,
            new
            {
                chunk.Id, chunk.RepoId, chunk.FilePath, chunk.Language,
                chunk.StartLine, chunk.EndLine, chunk.SymbolName,
                chunk.ChunkKind, chunk.ContentHash, chunk.EmbeddedAt, chunk.VectorId
            });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CodeChunkRecord>> GetChunksByRepoAsync(string repoId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(
            "SELECT * FROM code_chunks WHERE repo_id = @RepoId", new { RepoId = repoId });
        return rows.Select(MapChunk).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CodeChunkRecord>> GetChunksByVectorIdsAsync(IEnumerable<Guid> vectorIds, CancellationToken ct = default)
    {
        var ids = vectorIds.ToList();
        if (ids.Count == 0) return [];
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(
            "SELECT * FROM code_chunks WHERE vector_id = ANY(@Ids)", new { Ids = ids.ToArray() });
        return rows.Select(MapChunk).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteChunksByFileAsync(string repoId, string filePath, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM code_chunks WHERE repo_id = @RepoId AND file_path = @FilePath",
            new { RepoId = repoId, FilePath = filePath });
    }

    // ── Indexing jobs ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IndexingJobRecord?> GetNextPendingJobAsync(CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM indexing_jobs WHERE status = 'pending' ORDER BY started_at LIMIT 1");
        return row is null ? null : MapJob(row);
    }

    /// <inheritdoc />
    public async Task<IndexingJobRecord?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM indexing_jobs WHERE id = @Id", new { Id = jobId });
        return row is null ? null : MapJob(row);
    }

    /// <inheritdoc />
    public async Task CreateJobAsync(IndexingJobRecord job, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO indexing_jobs (id, repo_id, kind, status, started_at, finished_at, error)
            VALUES (@Id, @RepoId, @Kind, @Status, @StartedAt, @FinishedAt, @Error)
            """,
            new
            {
                job.Id,
                job.RepoId,
                Kind = job.Kind.ToString().ToLowerInvariant(),
                Status = job.Status.ToString().ToLowerInvariant(),
                job.StartedAt,
                job.FinishedAt,
                job.Error
            });
    }

    /// <inheritdoc />
    public async Task UpdateJobStatusAsync(Guid jobId, JobStatus status, string? error = null, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE indexing_jobs
            SET status = @Status,
                finished_at = CASE WHEN @Status IN ('completed','failed') THEN NOW() ELSE finished_at END,
                error = @Error
            WHERE id = @Id
            """,
            new { Id = jobId, Status = status.ToString().ToLowerInvariant(), Error = error });
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static RepositoryRecord MapRepo(dynamic r) => new(
        (string)r.id, (string)r.name, (string)r.url,
        (string)r.default_branch, (string?)r.language_hints,
        (DateTime)r.created_at, (DateTime)r.updated_at);

    private static IndexedFileRecord MapIndexedFile(dynamic r) => new(
        (Guid)r.id, (string)r.repo_id, (string)r.file_path,
        (string)r.content_hash, (DateTime)r.indexed_at);

    private static CodeChunkRecord MapChunk(dynamic r) => new(
        (Guid)r.id, (string)r.repo_id, (string)r.file_path,
        (string)r.language, (int)r.start_line, (int)r.end_line,
        (string?)r.symbol_name, (string)r.chunk_kind,
        (string)r.content_hash, (DateTime?)r.embedded_at, (Guid?)r.vector_id);

    private static IndexingJobRecord MapJob(dynamic r)
    {
        var kind = Enum.Parse<JobKind>(((string)r.kind), ignoreCase: true);
        var status = Enum.Parse<JobStatus>(((string)r.status), ignoreCase: true);
        return new(
            (Guid)r.id, (string)r.repo_id, kind, status,
            (DateTime)r.started_at, (DateTime?)r.finished_at, (string?)r.error);
    }
}
