using AgentHost.Indexing.Chunking;
using FluentAssertions;
using Xunit;

namespace AgentHost.Indexing.Tests.Unit;

public sealed class LineWindowChunkerTests
{
    [Fact]
    public void Chunk_ShortFile_ReturnsSingleChunk()
    {
        var sut = new LineWindowChunker(windowSize: 10, overlap: 2);
        var content = string.Join('\n', Enumerable.Range(1, 5).Select(i => $"line {i}"));

        var chunks = sut.Chunk("file.txt", content, "text").ToList();

        chunks.Should().HaveCount(1);
        chunks[0].StartLine.Should().Be(1);
        chunks[0].EndLine.Should().Be(5);
    }

    [Fact]
    public void Chunk_LongFile_ProducesCorrectOverlap()
    {
        var sut = new LineWindowChunker(windowSize: 10, overlap: 3);
        // 25 lines, window=10, step=7 → windows starting at 0, 7, 14, 21 → 4 chunks
        var content = string.Join('\n', Enumerable.Range(1, 25).Select(i => $"line {i}"));

        var chunks = sut.Chunk("file.txt", content, "text").ToList();

        chunks.Should().HaveCountGreaterThan(1);
        // Each chunk except possibly the last should have windowSize lines
        (chunks[0].EndLine - chunks[0].StartLine + 1).Should().Be(10);
        // Second chunk starts before end of first (overlap)
        chunks[1].StartLine.Should().BeLessThan(chunks[0].EndLine + 1);
    }

    [Fact]
    public void Chunk_EmptyContent_ReturnsNoChunks()
    {
        var sut = new LineWindowChunker();
        var chunks = sut.Chunk("empty.txt", string.Empty, "text").ToList();
        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Chunk_ContentHash_IsDeterministic()
    {
        var sut = new LineWindowChunker(windowSize: 5, overlap: 0);
        var content = "a\nb\nc\nd\ne";

        var chunks1 = sut.Chunk("f.txt", content, "text").ToList();
        var chunks2 = sut.Chunk("f.txt", content, "text").ToList();

        chunks1[0].ContentHash.Should().Be(chunks2[0].ContentHash);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_InvalidWindowSize_Throws(int windowSize)
    {
        var act = () => new LineWindowChunker(windowSize: windowSize);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
