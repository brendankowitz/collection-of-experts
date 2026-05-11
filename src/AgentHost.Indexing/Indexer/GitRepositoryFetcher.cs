using AgentHost.Indexing.Options;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHost.Indexing.Indexer;

/// <summary>
/// <see cref="IRepositoryFetcher"/> backed by LibGit2Sharp.
/// Clones repositories to a local cache directory and supports incremental
/// fetches via git diff.
/// </summary>
public sealed class GitRepositoryFetcher : IRepositoryFetcher
{
    private readonly string _cacheDir;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<GitRepositoryFetcher> _logger;

    /// <summary>Creates the fetcher from options.</summary>
    public GitRepositoryFetcher(IOptions<IndexingOptions> options, ILogger<GitRepositoryFetcher> logger)
    {
        _cacheDir = Path.GetFullPath(options.Value.RepoCacheDir);
        _maxFileSizeBytes = options.Value.MaxFileSizeBytes;
        _logger = logger;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <inheritdoc />
    public Task<FetchResult> FetchAsync(
        string repoId,
        string url,
        string? branch = null,
        string? sinceCommit = null,
        CancellationToken ct = default)
    {
        var localPath = Path.Combine(_cacheDir, SanitiseName(repoId));

        if (!Repository.IsValid(localPath))
        {
            _logger.LogInformation("Cloning {Url} into {Path}", url, localPath);
            Repository.Clone(url, localPath, new CloneOptions
            {
                BranchName = branch,
                RecurseSubmodules = false
            });
        }
        else
        {
            _logger.LogInformation("Fetching updates for {RepoId}", repoId);
            using var repo = new Repository(localPath);
            var remote = repo.Network.Remotes["origin"];
            Commands.Fetch(repo, remote.Name, [], null, null);
            if (!string.IsNullOrEmpty(branch))
            {
                var trackingBranch = repo.Branches[$"origin/{branch}"];
                if (trackingBranch != null)
                    Commands.Checkout(repo, trackingBranch.Tip.Sha);
            }
        }

        using var repository = new Repository(localPath);
        var headCommit = repository.Head.Tip?.Sha ?? "unknown";

        IReadOnlyDictionary<string, string> files;
        if (!string.IsNullOrEmpty(sinceCommit))
        {
            files = GetChangedFiles(repository, sinceCommit, localPath);
        }
        else
        {
            files = GetAllFiles(repository, localPath);
        }

        _logger.LogInformation("Fetched {Count} files from {RepoId} at {Sha}", files.Count, repoId, headCommit);
        return Task.FromResult(new FetchResult(headCommit, files));
    }

    private IReadOnlyDictionary<string, string> GetAllFiles(Repository repo, string localPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in repo.Index)
        {
            if (ShouldSkip(entry.Path)) continue;

            var fullPath = Path.Combine(localPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            var info = new FileInfo(fullPath);
            if (info.Length > _maxFileSizeBytes) continue;

            try { result[entry.Path] = File.ReadAllText(fullPath); }
            catch { /* skip unreadable files */ }
        }
        return result;
    }

    private IReadOnlyDictionary<string, string> GetChangedFiles(Repository repo, string sinceCommit, string localPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Commit? since = null;
        try { since = repo.Lookup<Commit>(sinceCommit); } catch { /* not found */ }

        if (since is null) return GetAllFiles(repo, localPath);

        var head = repo.Head.Tip;
        var diff = repo.Diff.Compare<TreeChanges>(since.Tree, head.Tree);

        foreach (var change in diff)
        {
            if (change.Status == ChangeKind.Deleted) continue;
            if (ShouldSkip(change.Path)) continue;

            var fullPath = Path.Combine(localPath, change.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            var info = new FileInfo(fullPath);
            if (info.Length > _maxFileSizeBytes) continue;

            try { result[change.Path] = File.ReadAllText(fullPath); }
            catch { /* skip */ }
        }
        return result;
    }

    private static bool ShouldSkip(string path)
    {
        // Skip binary-typical extensions and dot-directories.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (BinaryExtensions.Contains(ext)) return true;
        if (path.Contains("/.git/") || path.Contains("\\.git\\")) return true;
        if (path.StartsWith(".git/", StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".zip", ".tar", ".gz", ".7z", ".rar",
        ".exe", ".dll", ".so", ".dylib", ".bin",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".mp4", ".avi", ".mov", ".mkv",
        ".db", ".sqlite", ".lock"
    };

    private static string SanitiseName(string name)
        => string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
}
