using System.Security.Cryptography;
using System.Text;
using AgentHost.Indexing.Chunking;
using AgentHost.Indexing.Embeddings;
using AgentHost.Indexing.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentHost.Indexing.Indexer;

/// <summary>
/// Background service that polls for pending indexing jobs and processes them
/// one at a time per repository.
/// </summary>
public sealed class IndexingWorker : BackgroundService
{
    private readonly IMetadataStore _metadata;
    private readonly IVectorStore _vectors;
    private readonly IRepositoryFetcher _fetcher;
    private readonly ChunkerSelector _chunker;
    private readonly IEmbedder _embedder;
    private readonly ILogger<IndexingWorker> _logger;

    private const int EmbedBatchSize = 32;
    private const string CodeCollection = "code-chunks";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Creates the indexing worker.</summary>
    public IndexingWorker(
        IMetadataStore metadata,
        IVectorStore vectors,
        IRepositoryFetcher fetcher,
        ChunkerSelector chunker,
        IEmbedder embedder,
        ILogger<IndexingWorker> logger)
    {
        _metadata = metadata;
        _vectors = vectors;
        _fetcher = fetcher;
        _chunker = chunker;
        _embedder = embedder;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _metadata.MigrateAsync(stoppingToken);
            await _vectors.EnsureCollectionAsync(CodeCollection, (uint)_embedder.Dimensions, stoppingToken);
            _logger.LogInformation("IndexingWorker started, polling every {Interval}s", PollInterval.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IndexingWorker could not initialise storage — worker disabled. Start Qdrant and Postgres to enable indexing.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _metadata.GetNextPendingJobAsync(stoppingToken);
                if (job is not null)
                    await ProcessJobAsync(job, stoppingToken);
                else
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in IndexingWorker poll loop");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(IndexingJobRecord job, CancellationToken ct)
    {
        _logger.LogInformation("Starting {Kind} indexing job {JobId} for repo {RepoId}",
            job.Kind, job.Id, job.RepoId);

        await _metadata.UpdateJobStatusAsync(job.Id, JobStatus.Running, ct: ct);

        try
        {
            var repo = await _metadata.GetRepositoryAsync(job.RepoId, ct);
            if (repo is null)
                throw new InvalidOperationException($"Repository {job.RepoId} not found in metadata store");

            var fetchResult = await _fetcher.FetchAsync(
                job.RepoId, repo.Url, repo.DefaultBranch,
                sinceCommit: job.Kind == JobKind.Incremental ? null : null,
                ct: ct);

            int indexed = 0;
            int skipped = 0;

            foreach (var (filePath, content) in fetchResult.Files)
            {
                ct.ThrowIfCancellationRequested();

                var contentHash = ComputeHash(content);
                var existing = await _metadata.GetIndexedFileAsync(job.RepoId, filePath, ct);

                if (existing?.ContentHash == contentHash)
                {
                    skipped++;
                    continue;
                }

                var language = ChunkerSelector.GetLanguage(filePath) ?? "text";
                var chunks = _chunker.Chunk(filePath, content).ToList();

                if (chunks.Count == 0) { skipped++; continue; }

                // Delete old chunks for this file
                await _metadata.DeleteChunksByFileAsync(job.RepoId, filePath, ct);
                await _vectors.DeleteByFilterAsync(CodeCollection,
                    new Dictionary<string, string> { ["repo_id"] = job.RepoId, ["file_path"] = filePath }, ct);

                // Embed in batches
                for (int batchStart = 0; batchStart < chunks.Count; batchStart += EmbedBatchSize)
                {
                    var batch = chunks.Skip(batchStart).Take(EmbedBatchSize).ToList();
                    var vectors = await _embedder.EmbedAsync(batch.Select(c => c.Content), ct);

                    var points = new List<VectorPoint>();
                    for (int i = 0; i < batch.Count; i++)
                    {
                        var chunk = batch[i];
                        var vectorId = Guid.NewGuid();

                        points.Add(new VectorPoint(vectorId, vectors[i], new Dictionary<string, string>
                        {
                            ["repo_id"]     = job.RepoId,
                            ["file_path"]   = chunk.FilePath,
                            ["language"]    = chunk.Language,
                            ["start_line"]  = chunk.StartLine.ToString(),
                            ["end_line"]    = chunk.EndLine.ToString(),
                            ["symbol_name"] = chunk.SymbolName ?? string.Empty,
                            ["chunk_kind"]  = chunk.ChunkKind,
                            ["content"]     = chunk.Content.Length > 4096
                                ? chunk.Content[..4096]
                                : chunk.Content,
                        }));

                        await _metadata.UpsertChunkAsync(new CodeChunkRecord(
                            Guid.NewGuid(), job.RepoId, chunk.FilePath, chunk.Language,
                            chunk.StartLine, chunk.EndLine, chunk.SymbolName,
                            chunk.ChunkKind, chunk.ContentHash,
                            EmbeddedAt: DateTime.UtcNow, VectorId: vectorId), ct);
                    }

                    await _vectors.UpsertAsync(CodeCollection, points, ct);
                }

                await _metadata.UpsertIndexedFileAsync(new IndexedFileRecord(
                    Guid.NewGuid(), job.RepoId, filePath, contentHash, DateTime.UtcNow), ct);

                indexed++;
            }

            await _metadata.UpdateJobStatusAsync(job.Id, JobStatus.Completed, ct: ct);
            _logger.LogInformation("Job {JobId} complete: {Indexed} indexed, {Skipped} skipped",
                job.Id, indexed, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.Id);
            await _metadata.UpdateJobStatusAsync(job.Id, JobStatus.Failed, ex.Message);
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
