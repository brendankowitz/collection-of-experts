using AgentHost.Indexing.Embeddings;
using FluentAssertions;
using Xunit;

namespace AgentHost.Indexing.Tests.Unit;

public sealed class MockEmbedderTests
{
    [Fact]
    public async Task EmbedAsync_SameInput_ReturnsSameVector()
    {
        var sut = new MockEmbedder(dimensions: 64);
        const string text = "public class Foo { }";

        var v1 = (await sut.EmbedAsync([text]))[0];
        var v2 = (await sut.EmbedAsync([text]))[0];

        v1.Span.ToArray().Should().BeEquivalentTo(v2.Span.ToArray());
    }

    [Fact]
    public async Task EmbedAsync_DifferentInputs_ReturnDifferentVectors()
    {
        var sut = new MockEmbedder(dimensions: 64);

        var v1 = (await sut.EmbedAsync(["hello world"]))[0];
        var v2 = (await sut.EmbedAsync(["goodbye world"]))[0];

        v1.Span.ToArray().Should().NotBeEquivalentTo(v2.Span.ToArray());
    }

    [Fact]
    public async Task EmbedAsync_VectorIsNormalised()
    {
        var sut = new MockEmbedder(dimensions: 128);
        var v = (await sut.EmbedAsync(["test"]))[0];

        float norm = MathF.Sqrt(v.Span.ToArray().Sum(x => x * x));
        norm.Should().BeApproximately(1.0f, precision: 1e-4f);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsCorrectDimension()
    {
        var sut = new MockEmbedder(dimensions: 256);
        var v = (await sut.EmbedAsync(["any text"]))[0];

        v.Length.Should().Be(256);
    }

    [Fact]
    public async Task EmbedAsync_EmptyInput_ReturnsEmptyArray()
    {
        var sut = new MockEmbedder();
        var results = await sut.EmbedAsync([]);
        results.Should().BeEmpty();
    }
}
