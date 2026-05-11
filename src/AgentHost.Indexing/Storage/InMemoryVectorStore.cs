using System.Collections.Concurrent;

namespace AgentHost.Indexing.Storage;

/// <summary>
/// Thread-safe in-memory <see cref="IVectorStore"/> for unit tests.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, List<VectorPoint>> _collections = new();

    /// <inheritdoc />
    public Task EnsureCollectionAsync(string name, uint vectorSize, CancellationToken ct = default)
    {
        _collections.TryAdd(name, []);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertAsync(string collection, IEnumerable<VectorPoint> points, CancellationToken ct = default)
    {
        var store = _collections.GetOrAdd(collection, _ => []);
        lock (store)
        {
            foreach (var point in points)
            {
                store.RemoveAll(p => p.Id == point.Id);
                store.Add(point);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> queryVector,
        int k,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken ct = default)
    {
        if (!_collections.TryGetValue(collection, out var store))
            return Task.FromResult<IReadOnlyList<ScoredPoint>>([]);

        var qv = queryVector.Span.ToArray();

        List<VectorPoint> candidates;
        lock (store) { candidates = store.ToList(); }

        if (filter is { Count: > 0 })
            candidates = candidates.Where(p => filter.All(kv =>
                p.Payload.TryGetValue(kv.Key, out var v) && v == kv.Value)).ToList();

        var scored = candidates
            .Select(p => new ScoredPoint(p.Id, CosineSimilarity(qv, p.Vector.Span.ToArray()), p.Payload))
            .OrderByDescending(s => s.Score)
            .Take(k)
            .ToList();

        return Task.FromResult<IReadOnlyList<ScoredPoint>>(scored);
    }

    /// <inheritdoc />
    public Task DeleteByFilterAsync(string collection, IReadOnlyDictionary<string, string> filter, CancellationToken ct = default)
    {
        if (!_collections.TryGetValue(collection, out var store)) return Task.CompletedTask;
        lock (store)
        {
            store.RemoveAll(p => filter.All(kv =>
                p.Payload.TryGetValue(kv.Key, out var v) && v == kv.Value));
        }
        return Task.CompletedTask;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, na = 0f, nb = 0f;
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        float denom = MathF.Sqrt(na) * MathF.Sqrt(nb);
        return denom < 1e-10f ? 0f : dot / denom;
    }
}
