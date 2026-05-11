using System.Text.RegularExpressions;

namespace AgentHost.Indexing.Chunking;

/// <summary>
/// Language-aware chunker that identifies top-level declarations using
/// regex-based parsing patterns per language.  Produces one chunk per
/// top-level type or function; oversized symbols (> MaxLinesPerChunk) are
/// split into sub-windows.
/// </summary>
/// <remarks>
/// The class is named <c>TreeSitterChunker</c> to honour the intended
/// tree-sitter integration seam.  A future swap to native tree-sitter
/// bindings only requires changing the parsing implementation, not the
/// caller surface.
/// </remarks>
public sealed class TreeSitterChunker : IChunker
{
    private const int MaxLinesPerChunk = 50;
    private const int SplitWindowSize = 50;
    private const int SplitOverlap = 10;

    // ── Top-level declaration patterns per language ───────────────────────────

    private static readonly Regex CsharpDeclRegex = new(
        @"^\s*(public|internal|protected|private|file|sealed|abstract|static|partial|record|readonly)[\w\s<>\[\],?]*\s+(class|interface|enum|struct|record|delegate)\s+(\w+)|^\s*(public|internal|protected|private|static|async|virtual|override|abstract|sealed|extern)[\w\s<>\[\],?*]*\s+(\w+)\s*[\(<]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TypescriptDeclRegex = new(
        @"^(?:export\s+)?(?:default\s+)?(?:async\s+)?(?:function\s+(\w+)|class\s+(\w+)|interface\s+(\w+)|type\s+(\w+)|enum\s+(\w+)|const\s+(\w+)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[\w]+)\s*=>)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PythonDeclRegex = new(
        @"^(?:async\s+)?def\s+(\w+)|^class\s+(\w+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex JavaDeclRegex = new(
        @"^\s*(?:public|private|protected|static|abstract|final|synchronized)[\w\s<>\[\],?]*\s+(?:class|interface|enum|record)\s+(\w+)|^\s*(?:public|private|protected|static|abstract|final|synchronized)[\w\s<>\[\],?]+\s+(\w+)\s*\(",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex GoDeclRegex = new(
        @"^func\s+(?:\(\w+\s+\*?\w+\)\s+)?(\w+)\s*\(|^type\s+(\w+)\s+(?:struct|interface)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <inheritdoc />
    public IEnumerable<CodeChunk> Chunk(string filePath, string content, string language)
    {
        if (string.IsNullOrEmpty(content)) yield break;

        var lines = SplitLines(content);
        var pattern = GetPattern(language);

        if (pattern is null)
        {
            // Unsupported language — emit a single chunk (caller may fall back)
            foreach (var c in WindowChunk(filePath, language, lines, 0, lines.Length - 1, null, "file"))
                yield return c;
            yield break;
        }

        var declarations = FindDeclarations(content, pattern, lines);

        if (declarations.Count == 0)
        {
            foreach (var c in WindowChunk(filePath, language, lines, 0, lines.Length - 1, null, "file"))
                yield return c;
            yield break;
        }

        for (int i = 0; i < declarations.Count; i++)
        {
            int startLine = declarations[i].Line;
            int endLine = i + 1 < declarations.Count
                ? declarations[i + 1].Line - 1
                : lines.Length - 1;

            foreach (var chunk in WindowChunk(filePath, language, lines, startLine, endLine,
                         declarations[i].Name, declarations[i].Kind))
                yield return chunk;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] SplitLines(string content)
        => content.Split('\n');

    private static Regex? GetPattern(string language) => language.ToLowerInvariant() switch
    {
        "csharp" or "cs" => CsharpDeclRegex,
        "typescript" or "ts" or "tsx" => TypescriptDeclRegex,
        "javascript" or "js" or "jsx" => TypescriptDeclRegex,
        "python" or "py" => PythonDeclRegex,
        "java" => JavaDeclRegex,
        "go" => GoDeclRegex,
        _ => null
    };

    private sealed record Declaration(int Line, string? Name, string Kind);

    private static List<Declaration> FindDeclarations(string content, Regex pattern, string[] lines)
    {
        var result = new List<Declaration>();

        foreach (Match m in pattern.Matches(content))
        {
            int lineIdx = CountLines(content, m.Index);

            // Extract captured symbol name from any capture group
            string? name = null;
            for (int g = 1; g < m.Groups.Count; g++)
            {
                if (m.Groups[g].Success && !string.IsNullOrEmpty(m.Groups[g].Value))
                {
                    name = m.Groups[g].Value;
                    break;
                }
            }

            string kind = DetectKind(m.Value);
            result.Add(new Declaration(lineIdx, name, kind));
        }

        return result;
    }

    private static int CountLines(string content, int charIndex)
    {
        int count = 0;
        for (int i = 0; i < charIndex && i < content.Length; i++)
            if (content[i] == '\n') count++;
        return count;
    }

    private static string DetectKind(string declarationText)
    {
        var t = declarationText.ToLowerInvariant();
        if (t.Contains("class")) return "class";
        if (t.Contains("interface")) return "interface";
        if (t.Contains("enum")) return "enum";
        if (t.Contains("struct")) return "struct";
        if (t.Contains("record")) return "record";
        if (t.Contains("delegate")) return "delegate";
        if (t.Contains("type ")) return "type";
        return "function";
    }

    private static IEnumerable<CodeChunk> WindowChunk(
        string filePath, string language, string[] lines,
        int startLine, int endLine, string? symbolName, string kind)
    {
        int length = endLine - startLine + 1;
        if (length <= MaxLinesPerChunk)
        {
            yield return Build(filePath, language, lines, startLine, endLine, symbolName, kind);
            yield break;
        }

        // Split oversized symbol into overlapping windows
        int pos = startLine;
        while (pos <= endLine)
        {
            int end = Math.Min(pos + SplitWindowSize - 1, endLine);
            yield return Build(filePath, language, lines, pos, end, symbolName, $"{kind}-split");
            if (end >= endLine) break;
            pos += SplitWindowSize - SplitOverlap;
        }
    }

    private static CodeChunk Build(
        string filePath, string language, string[] lines,
        int startLine, int endLine, string? symbolName, string kind)
    {
        var slice = lines[startLine..(endLine + 1)];
        return new CodeChunk(filePath, language, startLine + 1, endLine + 1,
            symbolName, kind, string.Join('\n', slice));
    }
}
