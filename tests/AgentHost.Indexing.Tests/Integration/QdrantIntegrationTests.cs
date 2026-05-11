using AgentHost.Indexing.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Qdrant;
using Xunit;
using AgentHost.Indexing.Options;

// Alias to avoid ambiguity with AgentHost.Indexing.Options namespace.
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AgentHost.Indexing.Tests.Integration;

/// <summary>
/// Integration tests that require Docker.  Skipped in CI via
/// <c>--filter "Category!=Integration"</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class QdrantIntegrationTests : IAsyncLifetime
{
    private QdrantContainer? _container;

    public async Task InitializeAsync()
    {
        _container = new QdrantBuilder().Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task UpsertAndSearch_RoundTrip()
    {
        var opts = OptionsFactory.Create(new QdrantOptions
        {
            Host = GetContainer().Hostname,
            GrpcPort = GetContainer().GetMappedPublicPort(6334),
        });

        var store = new QdrantVectorStore(opts, NullLogger<QdrantVectorStore>.Instance);

        const string collection = "test-collection";
        const uint dims = 4;
        await store.EnsureCollectionAsync(collection, dims);

        var id = Guid.NewGuid();
        var vector = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f]);
        await store.UpsertAsync(collection,
        [
            new VectorPoint(id, vector, new Dictionary<string, string>
            {
                ["repo_id"]   = "my-repo",
                ["file_path"] = "src/Test.cs",
                ["content"]   = "hello world",
            })
        ]);

        var results = await store.SearchAsync(collection, vector, k: 1,
            filter: new Dictionary<string, string> { ["repo_id"] = "my-repo" });

        results.Should().ContainSingle();
        results[0].Id.Should().Be(id);
        results[0].Score.Should().BeApproximately(1.0f, precision: 0.01f);
        results[0].Payload["file_path"].Should().Be("src/Test.cs");
    }

    [DockerFact]
    public async Task DeleteByFilter_RemovesPoints()
    {
        var opts = OptionsFactory.Create(new QdrantOptions
        {
            Host = GetContainer().Hostname,
            GrpcPort = GetContainer().GetMappedPublicPort(6334),
        });

        var store = new QdrantVectorStore(opts, NullLogger<QdrantVectorStore>.Instance);

        const string collection = "delete-test";
        await store.EnsureCollectionAsync(collection, 4);

        var id = Guid.NewGuid();
        var v = new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]);
        await store.UpsertAsync(collection,
        [
            new VectorPoint(id, v, new Dictionary<string, string>
            {
                ["repo_id"] = "del-repo", ["file_path"] = "x.cs"
            })
        ]);

        await store.DeleteByFilterAsync(collection, new Dictionary<string, string> { ["repo_id"] = "del-repo" });

        var after = await store.SearchAsync(collection, v, k: 5);
        after.Should().BeEmpty();
    }

    private QdrantContainer GetContainer()
        => _container ?? throw new InvalidOperationException("Container has not been initialized.");
}
