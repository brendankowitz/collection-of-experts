namespace AgentHost.Indexing.Chunking;

/// <summary>
/// Splits source file content into indexable <see cref="CodeChunk"/> slices.
/// </summary>
public interface IChunker
{
    /// <summary>
    /// Chunks <paramref name="content"/> and returns the resulting code segments.
    /// </summary>
    /// <param name="filePath">Relative path used for context/display.</param>
    /// <param name="content">Full file text.</param>
    /// <param name="language">Lowercase language identifier (e.g., <c>csharp</c>, <c>python</c>).</param>
    IEnumerable<CodeChunk> Chunk(string filePath, string content, string language);
}

/// <summary>A single indexable fragment of source code.</summary>
public sealed record CodeChunk(
    string FilePath,
    string Language,
    int StartLine,
    int EndLine,
    string? SymbolName,
    string ChunkKind,
    string Content)
{
    /// <summary>Returns a SHA-256 hex hash of <see cref="Content"/>.</summary>
    public string ContentHash => ComputeHash(Content);

    private static string ComputeHash(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
