using AgentHost.Indexing.Chunking;
using FluentAssertions;
using Xunit;

namespace AgentHost.Indexing.Tests.Unit;

public sealed class TreeSitterChunkerTests
{
    private readonly TreeSitterChunker _sut = new();

    [Fact]
    public void Chunk_CsharpFileWithTwoClasses_ReturnsTwoOrMoreChunks()
    {
        const string code = """
            using System;

            namespace MyApp
            {
                public class Foo
                {
                    public void DoSomething()
                    {
                        Console.WriteLine("foo");
                    }
                }

                public class Bar
                {
                    public string GetValue() => "bar";

                    public int Compute(int x) => x * 2;
                }
            }
            """;

        var chunks = _sut.Chunk("MyFile.cs", code, "csharp").ToList();

        chunks.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Chunk_CsharpClass_HasCorrectLineRanges()
    {
        // 5-line class starting at line 1
        const string code = "public class A\n{\n    public void M() {}\n    public void N() {}\n}\n";

        var chunks = _sut.Chunk("A.cs", code, "csharp").ToList();

        chunks.Should().NotBeEmpty();
        foreach (var c in chunks)
        {
            c.StartLine.Should().BeGreaterThan(0);
            c.EndLine.Should().BeGreaterThanOrEqualTo(c.StartLine);
        }
    }

    [Fact]
    public void Chunk_EmptyContent_ReturnsNoChunks()
    {
        var chunks = _sut.Chunk("Empty.cs", string.Empty, "csharp").ToList();
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_PythonFile_DetectsClassesAndFunctions()
    {
        const string code = """
            class MyClass:
                def __init__(self):
                    pass

            def standalone_function(x):
                return x + 1
            """;

        var chunks = _sut.Chunk("mod.py", code, "python").ToList();
        chunks.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Chunk_OversizedSymbol_IsSplitIntoWindows()
    {
        // Generate a class with 60 lines (> MaxLinesPerChunk=50)
        var lines = Enumerable.Range(1, 60).Select(i => $"    public void Method{i}() {{}}");
        var code = "public class BigClass\n{\n" + string.Join('\n', lines) + "\n}";

        var chunks = _sut.Chunk("Big.cs", code, "csharp").ToList();

        chunks.Should().HaveCountGreaterThan(1, "oversized symbol should be split");
        chunks.All(c => c.EndLine - c.StartLine + 1 <= 55).Should().BeTrue();
    }
}
