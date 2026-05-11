using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using AgentHost.Repositories.Options;

namespace AgentHost.Repositories.Registry;

public sealed class PostgresRepositoryRegistry : IRepositoryRegistry
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresRepositoryRegistry> _logger;

    public event EventHandler<RepositoryChange>? Changed;

    public PostgresRepositoryRegistry(IOptions<RepositoriesOptions> options, ILogger<PostgresRepositoryRegistry> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
    }

    private NpgsqlConnection Open() => new(_connectionString);

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        const string sql = """
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS source TEXT NOT NULL DEFAULT 'github';
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS owner_or_org TEXT NOT NULL DEFAULT '';
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS clone_url TEXT NOT NULL DEFAULT '';
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS auth_secret_ref TEXT;
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS language_hints_arr TEXT[] NOT NULL DEFAULT '{}';
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS agent_persona TEXT NOT NULL DEFAULT '';
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS prompt_overrides JSONB NOT NULL DEFAULT '{}';
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS indexing_schedule TEXT;
            ALTER TABLE repositories ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE;
            """;

        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(sql);
        _logger.LogInformation("Phase-3 repository registry migration completed");
    }

    public async Task<Repository?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT * FROM repositories WHERE id = @Id", new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<IReadOnlyList<Repository>> ListAsync(bool? enabled = null, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        var whereSql = enabled.HasValue ? "WHERE enabled = @Enabled" : "";
        var rows = await conn.QueryAsync<dynamic>(
            $"SELECT * FROM repositories {whereSql} ORDER BY name",
            enabled.HasValue ? new { Enabled = enabled.Value } : null);
        return rows.Select(MapRow).ToList();
    }

    public async Task<Repository> CreateAsync(Repository repo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo.Id))
            repo.Id = Guid.NewGuid().ToString("N");
        repo.CreatedAtUtc = DateTime.UtcNow;
        repo.UpdatedAtUtc = DateTime.UtcNow;

        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO repositories
                (id, name, url, default_branch, language_hints, created_at, updated_at,
                 source, owner_or_org, clone_url, auth_secret_ref, language_hints_arr,
                 agent_persona, prompt_overrides, indexing_schedule, enabled)
            VALUES
                (@Id, @Name, @Url, @DefaultBranch, @LanguageHintsLegacy, @CreatedAtUtc, @UpdatedAtUtc,
                 @Source, @OwnerOrOrg, @CloneUrl, @AuthSecretRef, @LanguageHintsArr,
                 @AgentPersona, @PromptOverridesJson::jsonb, @IndexingSchedule, @Enabled)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name, url = EXCLUDED.url, default_branch = EXCLUDED.default_branch,
                language_hints = EXCLUDED.language_hints, updated_at = NOW(),
                source = EXCLUDED.source, owner_or_org = EXCLUDED.owner_or_org,
                clone_url = EXCLUDED.clone_url, auth_secret_ref = EXCLUDED.auth_secret_ref,
                language_hints_arr = EXCLUDED.language_hints_arr, agent_persona = EXCLUDED.agent_persona,
                prompt_overrides = EXCLUDED.prompt_overrides, indexing_schedule = EXCLUDED.indexing_schedule,
                enabled = EXCLUDED.enabled
            """,
            ToParams(repo));

        Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Added, repo));
        return repo;
    }

    public async Task<Repository> UpdateAsync(Repository repo, CancellationToken ct = default)
    {
        repo.UpdatedAtUtc = DateTime.UtcNow;
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("""
            UPDATE repositories SET
                name = @Name, url = @Url, default_branch = @DefaultBranch,
                language_hints = @LanguageHintsLegacy, updated_at = @UpdatedAtUtc,
                source = @Source, owner_or_org = @OwnerOrOrg, clone_url = @CloneUrl,
                auth_secret_ref = @AuthSecretRef, language_hints_arr = @LanguageHintsArr,
                agent_persona = @AgentPersona, prompt_overrides = @PromptOverridesJson::jsonb,
                indexing_schedule = @IndexingSchedule, enabled = @Enabled
            WHERE id = @Id
            """,
            ToParams(repo));

        Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Updated, repo));
        return repo;
    }

    public async Task DeleteAsync(string id, bool hard = false, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);

        if (hard)
        {
            await conn.ExecuteAsync("DELETE FROM repositories WHERE id = @Id", new { Id = id });
            var dummy = new Repository { Id = id };
            Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Removed, dummy));
        }
        else
        {
            await conn.ExecuteAsync(
                "UPDATE repositories SET enabled = FALSE, updated_at = NOW() WHERE id = @Id",
                new { Id = id });
            var updated = await GetAsync(id, ct);
            if (updated is not null)
                Changed?.Invoke(this, new RepositoryChange(RepositoryChangeKind.Updated, updated));
        }
    }

    private static object ToParams(Repository r) => new
    {
        r.Id,
        Name = $"{r.OwnerOrOrg}/{r.Name}",
        Url = r.CloneUrl,
        r.DefaultBranch,
        LanguageHintsLegacy = string.Join(",", r.LanguageHints),
        CreatedAtUtc = r.CreatedAtUtc,
        UpdatedAtUtc = r.UpdatedAtUtc,
        Source = r.Source.ToString().ToLowerInvariant(),
        r.OwnerOrOrg,
        r.CloneUrl,
        r.AuthSecretRef,
        LanguageHintsArr = r.LanguageHints,
        r.AgentPersona,
        PromptOverridesJson = System.Text.Json.JsonSerializer.Serialize(r.PromptOverrides),
        r.IndexingSchedule,
        r.Enabled
    };

    private static Repository MapRow(dynamic r)
    {
        var source = Enum.TryParse<RepositorySource>((string)r.source, true, out var s) ? s : RepositorySource.GitHub;
        var promptOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (r.prompt_overrides is string json && !string.IsNullOrEmpty(json))
                promptOverrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch { }

        string[] langHints = [];
        try
        {
            if (r.language_hints_arr is string[] arr) langHints = arr;
        }
        catch { }

        return new Repository
        {
            Id = (string)r.id,
            Source = source,
            OwnerOrOrg = (string)(r.owner_or_org ?? ""),
            Name = (string)r.name,
            DefaultBranch = (string)r.default_branch,
            CloneUrl = (string)(r.clone_url ?? r.url ?? ""),
            AuthSecretRef = (string?)r.auth_secret_ref,
            LanguageHints = langHints,
            AgentPersona = (string)(r.agent_persona ?? ""),
            PromptOverrides = promptOverrides,
            IndexingSchedule = (string?)r.indexing_schedule,
            Enabled = (bool)(r.enabled ?? true),
            CreatedAtUtc = (DateTime)r.created_at,
            UpdatedAtUtc = (DateTime)r.updated_at
        };
    }
}
