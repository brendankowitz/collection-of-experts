using System.Security.Cryptography;
using System.Text;

namespace AgentHost.Indexing.Embeddings;

/// <summary>
/// Deterministic hash-based pseudo-embedder for tests.
/// Returns a normalised float vector derived from the SHA-256 of the input text.
/// Same input → same vector; different inputs → different vectors.
/// </summary>
public sealed class MockEmbedder : IEmbedder
{
    private readonly int _dimensions;

    /// <summary>Creates a mock embedder with the given vector size (default 1536).</summary>
    public MockEmbedder(int dimensions = 1536)
    {
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        _dimensions = dimensions;
    }

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>[]> EmbedAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = texts.Select(BuildVector).ToArray();
        return Task.FromResult(results);
    }

    private ReadOnlyMemory<float> BuildVector(string text)
    {
        // Seed a deterministic sequence with SHA-256 of the text.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        var floats = new float[_dimensions];
        float sumSq = 0f;

        for (int i = 0; i < _dimensions; i++)
        {
            // Cycle through the 32-byte hash repeatedly.
            byte b = hash[i % hash.Length];
            // Mix index into the byte to get different values per dimension.
            float v = ((b ^ (i & 0xFF)) / 255f) * 2f - 1f;
            floats[i] = v;
            sumSq += v * v;
        }

        // L2-normalise so cosine similarity is well-defined.
        float norm = MathF.Sqrt(sumSq);
        if (norm > 1e-8f)
            for (int i = 0; i < _dimensions; i++)
                floats[i] /= norm;

        return floats;
    }
}
