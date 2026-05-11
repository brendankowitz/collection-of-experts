using System.Net.Http.Headers;
using System.Text;
using AgentHost.Repositories.Options;
using AgentHost.Repositories.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHost.Repositories.Sources;

public sealed class AzureDevOpsRepositorySource : IRepositorySource
{
    private readonly AzureDevOpsSourceOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<AzureDevOpsRepositorySource> _logger;

    public AzureDevOpsRepositorySource(
        IOptions<RepositoriesOptions> options,
        HttpClient http,
        ILogger<AzureDevOpsRepositorySource> logger)
    {
        _options = options.Value.Sources.AzureDevOps;
        _http = http;
        _logger = logger;
        if (!string.IsNullOrEmpty(_options.Pat))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.Pat}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    public async Task<RepositoryMetadata> ProbeAsync(Registry.Repository repo, CancellationToken ct = default)
    {
        try
        {
            var org = _options.Organization ?? repo.OwnerOrOrg;
            var url = $"https://dev.azure.com/{org}/{repo.OwnerOrOrg}/_apis/git/repositories/{repo.Name}?api-version=7.1";
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ADO probe returned {Status} for {Repo}", response.StatusCode, repo.Name);
                return new RepositoryMetadata($"{repo.OwnerOrOrg}/{repo.Name}", repo.DefaultBranch, repo.CloneUrl, string.Empty, true);
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new RepositoryMetadata(
                root.TryGetProperty("name", out var n) ? n.GetString() ?? repo.Name : repo.Name,
                root.TryGetProperty("defaultBranch", out var db) ? db.GetString()?.Replace("refs/heads/", "") ?? repo.DefaultBranch : repo.DefaultBranch,
                root.TryGetProperty("remoteUrl", out var ru) ? ru.GetString() ?? repo.CloneUrl : repo.CloneUrl,
                string.Empty,
                true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe ADO repo {Name}", repo.Name);
            return new RepositoryMetadata($"{repo.OwnerOrOrg}/{repo.Name}", repo.DefaultBranch, repo.CloneUrl, string.Empty, true);
        }
    }

    public Task<Stream> FetchTarballAsync(Registry.Repository repo, string @ref, CancellationToken ct = default)
    {
        return Task.FromResult<Stream>(Stream.Null);
    }
}
