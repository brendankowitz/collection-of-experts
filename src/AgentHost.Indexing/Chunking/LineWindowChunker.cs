namespace AgentHost.Indexing.Chunking;

/// <summary>
/// Fallback chunker that slides fixed-size line windows over any source file.
/// Used when the language is unknown or when tree-sitter parsing fails.
/// </summary>
public sealed class LineWindowChunker : IChunker
{
    private readonly int _windowSize;
    private readonly int _overlap;

    /// <summary>Creates a line-window chunker.</summary>
    /// <param name="windowSize">Lines per chunk (default 200).</param>
    /// <param name="overlap">Overlap lines between consecutive windows (default 20).</param>
    public LineWindowChunker(int windowSize = 200, int overlap = 20)
    {
        if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize));
        if (overlap < 0 || overlap >= windowSize) throw new ArgumentOutOfRangeException(nameof(overlap));
        _windowSize = windowSize;
        _overlap = overlap;
    }

    /// <inheritdoc />
    public IEnumerable<CodeChunk> Chunk(string filePath, string content, string language)
    {
        if (string.IsNullOrEmpty(content)) yield break;

        var lines = content.Split('\n');
        int step = _windowSize - _overlap;
        int pos = 0;

        while (pos < lines.Length)
        {
            int end = Math.Min(pos + _windowSize - 1, lines.Length - 1);
            var slice = lines[pos..(end + 1)];
            yield return new CodeChunk(
                filePath, language,
                StartLine: pos + 1,
                EndLine: end + 1,
                SymbolName: null,
                ChunkKind: "window",
                Content: string.Join('\n', slice));

            if (end >= lines.Length - 1) break;
            pos += step;
        }
    }
}
