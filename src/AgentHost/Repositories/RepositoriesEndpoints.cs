using AgentHost.Indexing.Storage;
using AgentHost.Repositories.Registry;
using AgentHost.Repositories.Sources;

namespace AgentHost.Repositories;

public static class RepositoriesEndpoints
{
    public static WebApplication MapRepositoriesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/repositories")
            .RequireAuthorization("RepositoryAdmin");

        group.MapGet("/", async (IRepositoryRegistry registry, bool? enabled, CancellationToken ct) =>
        {
            var repos = await registry.ListAsync(enabled, ct);
            return Results.Ok(repos);
        });

        group.MapGet("/{id}", async (string id, IRepositoryRegistry registry, CancellationToken ct) =>
        {
            var repo = await registry.GetAsync(id, ct);
            return repo is null ? Results.NotFound() : Results.Ok(repo);
        });

        group.MapPost("/", async (
            CreateRepositoryRequest request,
            IRepositoryRegistry registry,
            RepositorySourceFactory sourceFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.OwnerOrOrg) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("OwnerOrOrg and Name are required.");

            if (!TryParseSource(request.Source, out var source))
                return Results.BadRequest($"Invalid source '{request.Source}'. Must be 'github' or 'azure_devops'.");

            if (!string.IsNullOrEmpty(request.CloneUrl) && !Uri.TryCreate(request.CloneUrl, UriKind.Absolute, out _))
                return Results.BadRequest("CloneUrl must be a valid absolute URL.");

            var repo = new Registry.Repository
            {
                Source = source,
                OwnerOrOrg = request.OwnerOrOrg,
                Name = request.Name,
                DefaultBranch = request.DefaultBranch ?? "main",
                CloneUrl = request.CloneUrl ?? BuildDefaultCloneUrl(source, request.OwnerOrOrg, request.Name),
                AuthSecretRef = request.AuthSecretRef,
                LanguageHints = request.LanguageHints ?? [],
                AgentPersona = request.AgentPersona ?? $"{request.Name} Expert",
                PromptOverrides = request.PromptOverrides ?? new Dictionary<string, string>(StringComparer.Ordinal),
                IndexingSchedule = request.IndexingSchedule,
                Enabled = request.Enabled ?? true
            };

            try
            {
                var adapter = sourceFactory.GetSource(source);
                var metadata = await adapter.ProbeAsync(repo, ct);
                if (string.IsNullOrEmpty(repo.CloneUrl))
                    repo.CloneUrl = metadata.CloneUrl;
                if (string.IsNullOrEmpty(repo.DefaultBranch))
                    repo.DefaultBranch = metadata.DefaultBranch;
            }
            catch { }

            var created = await registry.CreateAsync(repo, ct);
            return Results.Created($"/api/repositories/{created.Id}", created);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateRepositoryRequest request,
            IRepositoryRegistry registry,
            CancellationToken ct) =>
        {
            var existing = await registry.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();

            if (request.DefaultBranch is not null) existing.DefaultBranch = request.DefaultBranch;
            if (request.CloneUrl is not null)
            {
                if (!Uri.TryCreate(request.CloneUrl, UriKind.Absolute, out _))
                    return Results.BadRequest("CloneUrl must be a valid absolute URL.");
                existing.CloneUrl = request.CloneUrl;
            }
            if (request.AuthSecretRef is not null) existing.AuthSecretRef = request.AuthSecretRef;
            if (request.LanguageHints is not null) existing.LanguageHints = request.LanguageHints;
            if (request.AgentPersona is not null) existing.AgentPersona = request.AgentPersona;
            if (request.PromptOverrides is not null) existing.PromptOverrides = request.PromptOverrides;
            if (request.IndexingSchedule is not null) existing.IndexingSchedule = request.IndexingSchedule;
            if (request.Enabled is not null) existing.Enabled = request.Enabled.Value;

            var updated = await registry.UpdateAsync(existing, ct);
            return Results.Ok(updated);
        });

        group.MapDelete("/{id}", async (string id, bool? hard, IRepositoryRegistry registry, CancellationToken ct) =>
        {
            var existing = await registry.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            await registry.DeleteAsync(id, hard ?? false, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id}/index", async (
            string id,
            IRepositoryRegistry registry,
            IMetadataStore metadataStore,
            CancellationToken ct) =>
        {
            var repo = await registry.GetAsync(id, ct);
            if (repo is null) return Results.NotFound();

            var job = new IndexingJobRecord(
                Guid.NewGuid(),
                id,
                JobKind.Full,
                JobStatus.Pending,
                DateTime.UtcNow,
                null,
                null);

            await metadataStore.CreateJobAsync(job, ct);
            return Results.Accepted($"/api/repositories/{id}/index/{job.Id}", new { jobId = job.Id });
        });

        return app;
    }

    private static bool TryParseSource(string? source, out AgentHost.Repositories.Registry.RepositorySource result)
    {
        result = AgentHost.Repositories.Registry.RepositorySource.GitHub;
        if (source is null) return true;
        return source.ToLowerInvariant() switch
        {
            "github" => SetAndReturn(AgentHost.Repositories.Registry.RepositorySource.GitHub, out result),
            "azure_devops" or "azuredevops" => SetAndReturn(AgentHost.Repositories.Registry.RepositorySource.AzureDevOps, out result),
            _ => false
        };
    }

    private static bool SetAndReturn(AgentHost.Repositories.Registry.RepositorySource value, out AgentHost.Repositories.Registry.RepositorySource result)
    {
        result = value;
        return true;
    }

    private static string BuildDefaultCloneUrl(AgentHost.Repositories.Registry.RepositorySource source, string owner, string name) =>
        source == AgentHost.Repositories.Registry.RepositorySource.GitHub
            ? $"https://github.com/{owner}/{name}.git"
            : string.Empty;
}

public sealed record CreateRepositoryRequest(
    string? Source,
    string OwnerOrOrg,
    string Name,
    string? DefaultBranch,
    string? CloneUrl,
    string? AuthSecretRef,
    string[]? LanguageHints,
    string? AgentPersona,
    Dictionary<string, string>? PromptOverrides,
    string? IndexingSchedule,
    bool? Enabled);

public sealed record UpdateRepositoryRequest(
    string? DefaultBranch,
    string? CloneUrl,
    string? AuthSecretRef,
    string[]? LanguageHints,
    string? AgentPersona,
    Dictionary<string, string>? PromptOverrides,
    string? IndexingSchedule,
    bool? Enabled);
