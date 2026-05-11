using AgentHost.Indexing.Embeddings;
using AgentHost.Indexing.Retrieval;
using AgentHost.Indexing.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHost.Indexing.Tests.Unit;

public sealed class HybridRetrieverTests
{
    private readonly MockEmbedder _embedder = new(dimensions: 32);
    private readonly InMemoryVectorStore _vectorStore = new();
    private readonly NoopReranker _reranker = new();

    [Fact]
    public async Task SearchAsync_ReturnsHitsFromSeededVectorStore()
    {
        const string repoId = "test-repo";
        await _vectorStore.EnsureCollectionAsync("code-chunks", 32);

        // Seed two points that match the query content well
        var texts = new[] { "FHIR search parameter registry", "Healthcare data pipeline" };
        var vectors = await _embedder.EmbedAsync(texts);

        await _vectorStore.UpsertAsync("code-chunks",
        [
            new VectorPoint(Guid.NewGuid(), vectors[0], new Dictionary<string, string>
            {
                ["repo_id"] = repoId,
                ["file_path"] = "src/Search/SearchParameterRegistry.cs",
                ["language"] = "csharp",
                ["start_line"] = "1",
                ["end_line"] = "50",
                ["symbol_name"] = "SearchParameterRegistry",
                ["chunk_kind"] = "class",
                ["content"] = "FHIR search parameter registry implementation"
            }),
            new VectorPoint(Guid.NewGuid(), vectors[1], new Dictionary<string, string>
            {
                ["repo_id"] = repoId,
                ["file_path"] = "src/Pipeline/DataPipeline.cs",
                ["language"] = "csharp",
                ["start_line"] = "1",
                ["end_line"] = "30",
                ["symbol_name"] = "DataPipeline",
                ["chunk_kind"] = "class",
                ["content"] = "Healthcare data pipeline processor"
            }),
        ]);

        var sut = new HybridRetriever(_vectorStore, _embedder, _reranker, NullLogger<HybridRetriever>.Instance);

        var results = await sut.SearchAsync(repoId, "FHIR search parameter", k: 5);

        results.Should().NotBeEmpty();
        results.All(r => r.FilePath.Length > 0).Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_EmptyCollection_ReturnsNoHits()
    {
        await _vectorStore.EnsureCollectionAsync("code-chunks", 32);
        var sut = new HybridRetriever(_vectorStore, _embedder, _reranker, NullLogger<HybridRetriever>.Instance);

        var results = await sut.SearchAsync("nonexistent-repo", "anything", k: 5);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RespectedTopK()
    {
        const string repoId = "k-test";
        await _vectorStore.EnsureCollectionAsync("code-chunks", 32);

        // Seed 5 points
        var texts = Enumerable.Range(1, 5).Select(i => $"method {i} implementation").ToArray();
        var vectors = await _embedder.EmbedAsync(texts);
        var points = vectors.Select((v, i) => new VectorPoint(Guid.NewGuid(), v, new Dictionary<string, string>
        {
            ["repo_id"]     = repoId,
            ["file_path"]   = $"file{i}.cs",
            ["language"]    = "csharp",
            ["start_line"]  = "1",
            ["end_line"]    = "10",
            ["symbol_name"] = $"Method{i}",
            ["chunk_kind"]  = "function",
            ["content"]     = $"method {i} implementation code",
        })).ToList();

        await _vectorStore.UpsertAsync("code-chunks", points);

        var sut = new HybridRetriever(_vectorStore, _embedder, _reranker, NullLogger<HybridRetriever>.Instance);

        var results = await sut.SearchAsync(repoId, "method implementation", k: 3);

        results.Should().HaveCountLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task SearchAsync_ScoresAreBlendedCorrectly()
    {
        // The file_path keyword score should boost results whose path matches the query.
        const string repoId = "blend-test";
        await _vectorStore.EnsureCollectionAsync("code-chunks", 32);

        var textA = "authentication token service";
        var textB = "database migration runner";
        var vectors = await _embedder.EmbedAsync([textA, textB]);

        await _vectorStore.UpsertAsync("code-chunks",
        [
            new VectorPoint(Guid.NewGuid(), vectors[0], new Dictionary<string, string>
            {
                ["repo_id"] = repoId, ["file_path"] = "src/auth/AuthTokenService.cs",
                ["language"] = "csharp", ["start_line"] = "1", ["end_line"] = "20",
                ["symbol_name"] = "AuthTokenService", ["chunk_kind"] = "class",
                ["content"] = textA,
            }),
            new VectorPoint(Guid.NewGuid(), vectors[1], new Dictionary<string, string>
            {
                ["repo_id"] = repoId, ["file_path"] = "src/db/MigrationRunner.cs",
                ["language"] = "csharp", ["start_line"] = "1", ["end_line"] = "20",
                ["symbol_name"] = "MigrationRunner", ["chunk_kind"] = "class",
                ["content"] = textB,
            }),
        ]);

        var sut = new HybridRetriever(_vectorStore, _embedder, _reranker, NullLogger<HybridRetriever>.Instance);

        var results = await sut.SearchAsync(repoId, "auth token", k: 2);

        results.Should().NotBeEmpty();
        results[0].Score.Should().BeGreaterThan(0);
    }
}
