namespace AgentHost.Indexing.Storage;

/// <summary>
/// Abstraction over a vector database collection.
/// </summary>
public interface IVectorStore
{
    /// <summary>Creates the collection if it does not already exist.</summary>
    Task EnsureCollectionAsync(string name, uint vectorSize, CancellationToken ct = default);

    /// <summary>Inserts or replaces points in the collection.</summary>
    Task UpsertAsync(string collection, IEnumerable<VectorPoint> points, CancellationToken ct = default);

    /// <summary>Returns the top-<paramref name="k"/> nearest neighbours.</summary>
    Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> queryVector,
        int k,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken ct = default);

    /// <summary>Deletes all points matching <paramref name="filter"/> (AND of exact-match conditions).</summary>
    Task DeleteByFilterAsync(string collection, IReadOnlyDictionary<string, string> filter, CancellationToken ct = default);
}

/// <summary>A point to upsert into the vector store.</summary>
public sealed record VectorPoint(
    Guid Id,
    ReadOnlyMemory<float> Vector,
    Dictionary<string, string> Payload);

/// <summary>A scored search result from the vector store.</summary>
public sealed record ScoredPoint(
    Guid Id,
    float Score,
    IReadOnlyDictionary<string, string> Payload);
