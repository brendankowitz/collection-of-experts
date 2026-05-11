namespace AgentHost.Indexing.Chunking;

/// <summary>
/// Selects the appropriate <see cref="IChunker"/> based on file extension,
/// falling back to <see cref="LineWindowChunker"/> for unknown file types.
/// </summary>
public sealed class ChunkerSelector
{
    private readonly TreeSitterChunker _treeSitter;
    private readonly LineWindowChunker _lineWindow;

    private static readonly Dictionary<string, string> ExtToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]    = "csharp",
        [".ts"]    = "typescript",
        [".tsx"]   = "typescript",
        [".js"]    = "javascript",
        [".jsx"]   = "javascript",
        [".mjs"]   = "javascript",
        [".cjs"]   = "javascript",
        [".py"]    = "python",
        [".java"]  = "java",
        [".go"]    = "go",
    };

    /// <summary>Creates a new selector.</summary>
    public ChunkerSelector(TreeSitterChunker treeSitter, LineWindowChunker lineWindow)
    {
        _treeSitter = treeSitter;
        _lineWindow = lineWindow;
    }

    /// <summary>
    /// Returns chunks for <paramref name="filePath"/> using tree-sitter for
    /// known languages, or a line-window chunker for unknown ones.
    /// </summary>
    public IEnumerable<CodeChunk> Chunk(string filePath, string content)
    {
        var ext = Path.GetExtension(filePath);
        if (ExtToLanguage.TryGetValue(ext, out var language))
        {
            IEnumerable<CodeChunk> chunks;
            try
            {
                chunks = _treeSitter.Chunk(filePath, content, language).ToList();
            }
            catch
            {
                chunks = _lineWindow.Chunk(filePath, content, language);
            }
            return chunks;
        }

        return _lineWindow.Chunk(filePath, content, "text");
    }

    /// <summary>Maps a file extension to a canonical language name.</summary>
    public static string? GetLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ExtToLanguage.TryGetValue(ext, out var lang) ? lang : null;
    }

    /// <summary>Returns <c>true</c> if the extension is a recognised source language.</summary>
    public static bool IsSourceFile(string filePath)
        => ExtToLanguage.ContainsKey(Path.GetExtension(filePath));
}
