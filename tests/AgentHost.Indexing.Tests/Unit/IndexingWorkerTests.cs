using AgentHost.Indexing.Chunking;
using AgentHost.Indexing.Embeddings;
using AgentHost.Indexing.Indexer;
using AgentHost.Indexing.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHost.Indexing.Tests.Unit;

public sealed class IndexingWorkerTests
{
    private sealed class FakeRepositoryFetcher : IRepositoryFetcher
    {
        private readonly Dictionary<string, string> _files;
        public FakeRepositoryFetcher(Dictionary<string, string> files) => _files = files;

        public Task<FetchResult> FetchAsync(
            string repoId, string url, string? branch = null,
            string? sinceCommit = null, CancellationToken ct = default)
            => Task.FromResult(new FetchResult("abc123", _files));
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesPendingJob_IndexesFiles()
    {
        // Arrange
        var metadata = new InMemoryMetadataStore();
        var vectors = new InMemoryVectorStore();
        var embedder = new MockEmbedder(dimensions: 32);
        var chunker = new ChunkerSelector(new TreeSitterChunker(), new LineWindowChunker());

        const string repoId = "test-repo";
        await metadata.UpsertRepositoryAsync(new Storage.RepositoryRecord(
            repoId, "Test Repo", "https://github.com/test/repo", "main", null,
            DateTime.UtcNow, DateTime.UtcNow));

        var jobId = Guid.NewGuid();
        await metadata.CreateJobAsync(new Storage.IndexingJobRecord(
            jobId, repoId, Storage.JobKind.Full, Storage.JobStatus.Pending,
            DateTime.UtcNow, null, null));

        var files = new Dictionary<string, string>
        {
            ["src/Foo.cs"] = """
                public class Foo
                {
                    public void DoWork() { }
                }
                """,
            ["src/Bar.cs"] = "public class Bar { public string Name { get; set; } = string.Empty; }",
        };

        var fetcher = new FakeRepositoryFetcher(files);
        var worker = new IndexingWorker(
            metadata, vectors, fetcher, chunker, embedder,
            NullLogger<IndexingWorker>.Instance);

        // Act — run until no more pending jobs
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var task = worker.StartAsync(cts.Token);
        // Give the worker time to pick up and process the job
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var job = await metadata.GetJobAsync(jobId);
        job.Should().NotBeNull();
        job!.Status.Should().Be(Storage.JobStatus.Completed);

        var chunks = await metadata.GetChunksByRepoAsync(repoId);
        chunks.Should().NotBeEmpty("indexed files should have chunks in the metadata store");
    }

    [Fact]
    public async Task ExecuteAsync_IdempotentOnSameHash_SkipsExistingChunks()
    {
        var metadata = new InMemoryMetadataStore();
        var vectors = new InMemoryVectorStore();
        var embedder = new MockEmbedder(dimensions: 32);
        var chunker = new ChunkerSelector(new TreeSitterChunker(), new LineWindowChunker());

        const string repoId = "idempotent-repo";
        await metadata.UpsertRepositoryAsync(new Storage.RepositoryRecord(
            repoId, "Idempotent Repo", "https://github.com/x/y", "main", null,
            DateTime.UtcNow, DateTime.UtcNow));

        var files = new Dictionary<string, string> { ["A.cs"] = "public class A { }" };

        // Pre-seed the indexed file with the same hash
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(files["A.cs"]));
        var contentHash = Convert.ToHexString(hash).ToLowerInvariant();
        await metadata.UpsertIndexedFileAsync(new Storage.IndexedFileRecord(
            Guid.NewGuid(), repoId, "A.cs", contentHash, DateTime.UtcNow));

        var jobId = Guid.NewGuid();
        await metadata.CreateJobAsync(new Storage.IndexingJobRecord(
            jobId, repoId, Storage.JobKind.Full, Storage.JobStatus.Pending,
            DateTime.UtcNow, null, null));

        var fetcher = new FakeRepositoryFetcher(files);
        var worker = new IndexingWorker(
            metadata, vectors, fetcher, chunker, embedder,
            NullLogger<IndexingWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        await worker.StopAsync(CancellationToken.None);

        // Chunks should still be empty since we skipped the already-indexed file
        var chunks = await metadata.GetChunksByRepoAsync(repoId);
        chunks.Should().BeEmpty("unchanged file should be skipped");
    }
}
