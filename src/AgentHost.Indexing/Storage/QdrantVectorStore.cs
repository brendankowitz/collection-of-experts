using AgentHost.Indexing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

// Alias the Qdrant gRPC ScoredPoint to avoid ambiguity with our storage record.
using QdrantHit = Qdrant.Client.Grpc.ScoredPoint;

namespace AgentHost.Indexing.Storage;

/// <summary>
/// <see cref="IVectorStore"/> implementation backed by Qdrant.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;

    /// <summary>Initialises a new Qdrant vector store from options.</summary>
    public QdrantVectorStore(IOptions<QdrantOptions> options, ILogger<QdrantVectorStore> logger)
    {
        var opt = options.Value;
        _client = string.IsNullOrEmpty(opt.ApiKey)
            ? new QdrantClient(opt.Host, opt.GrpcPort)
            : new QdrantClient(opt.Host, opt.GrpcPort, apiKey: opt.ApiKey);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureCollectionAsync(string name, uint vectorSize, CancellationToken ct = default)
    {
        try
        {
            var collections = await _client.ListCollectionsAsync(ct);
            if (collections.Any(c => c == name))
                return;

            await _client.CreateCollectionAsync(name,
                new VectorParams { Size = vectorSize, Distance = Distance.Cosine },
                cancellationToken: ct);

            _logger.LogInformation("Created Qdrant collection {Collection} with vector size {Size}", name, vectorSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Qdrant collection {Collection}", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(string collection, IEnumerable<VectorPoint> points, CancellationToken ct = default)
    {
        var qdrantPoints = new List<PointStruct>();
        foreach (var p in points)
        {
            var ps = new PointStruct
            {
                Id = new PointId { Uuid = p.Id.ToString() },
                Vectors = p.Vector.ToArray(),
            };
            foreach (var kv in p.Payload)
                ps.Payload[kv.Key] = kv.Value;
            qdrantPoints.Add(ps);
        }

        if (qdrantPoints.Count == 0) return;

        await _client.UpsertAsync(collection, qdrantPoints, cancellationToken: ct);
        _logger.LogDebug("Upserted {Count} points into {Collection}", qdrantPoints.Count, collection);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> queryVector,
        int k,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken ct = default)
    {
        Filter? qdrantFilter = null;
        if (filter is { Count: > 0 })
        {
            var conditions = filter.Select(kv => new Condition
            {
                Field = new FieldCondition
                {
                    Key = kv.Key,
                    Match = new Match { Keyword = kv.Value }
                }
            }).ToList();
            qdrantFilter = new Filter();
            qdrantFilter.Must.AddRange(conditions);
        }

        IReadOnlyList<QdrantHit> results = await _client.SearchAsync(
            collection,
            queryVector.ToArray(),
            limit: (ulong)k,
            filter: qdrantFilter,
            cancellationToken: ct);

        return results.Select(r => new ScoredPoint(
            Guid.Parse(r.Id.Uuid),
            r.Score,
            r.Payload.ToDictionary(kv => kv.Key, kv => kv.Value.StringValue ?? string.Empty)
        )).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteByFilterAsync(string collection, IReadOnlyDictionary<string, string> filter, CancellationToken ct = default)
    {
        if (filter.Count == 0) return;

        var conditions = filter.Select(kv => new Condition
        {
            Field = new FieldCondition
            {
                Key = kv.Key,
                Match = new Match { Keyword = kv.Value }
            }
        }).ToList();

        var qdrantFilter = new Filter();
        qdrantFilter.Must.AddRange(conditions);

        await _client.DeleteAsync(collection, qdrantFilter, cancellationToken: ct);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
