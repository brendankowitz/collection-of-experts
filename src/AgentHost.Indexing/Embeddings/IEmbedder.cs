namespace AgentHost.Indexing.Embeddings;

/// <summary>
/// Converts text into dense embedding vectors.
/// </summary>
public interface IEmbedder
{
    /// <summary>The dimensionality of the vectors produced by this embedder.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds <paramref name="texts"/> in batches and returns one vector per input string.
    /// The order of results matches the order of inputs.
    /// </summary>
    Task<ReadOnlyMemory<float>[]> EmbedAsync(IEnumerable<string> texts, CancellationToken ct = default);
}
