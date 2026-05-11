using AgentHost.Indexing.Options;
using AgentHost.Indexing.Retrieval;
using AgentHost.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

// Alias to avoid ambiguity between AgentHost.Indexing.Options namespace and the Options static class.
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AgentHost.Indexing.Tests.Unit;

public sealed class MockCodeIndexServiceCompatTests
{
    private sealed class FakeRetriever : IRetriever
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<RetrievalHit>> SearchAsync(
            string repoId, string query, int k, CancellationToken ct = default)
        {
            CallCount++;
            IReadOnlyList<RetrievalHit> results =
            [
                new RetrievalHit("src/Real.cs", "real snippet", 0.9f, 1, 10, "csharp", "RealClass")
            ];
            return Task.FromResult(results);
        }
    }

    [Fact]
    public void Search_FlagOff_UsesMockBehavior()
    {
        var opts = OptionsFactory.Create(new IndexingOptions { UseRealRetriever = false });
        var fakeRetriever = new FakeRetriever();
        var sut = new MockCodeIndexServiceCompat(opts, fakeRetriever);

        // Mock behaviour: search for something in the seeded fhir-server repo
        var results = sut.Search("fhir-server", "search parameter");

        // The mock was not called (flag is off)
        fakeRetriever.CallCount.Should().Be(0);
    }

    [Fact]
    public void Search_FlagOn_DelegatesToRetriever()
    {
        var opts = OptionsFactory.Create(new IndexingOptions { UseRealRetriever = true });
        var fakeRetriever = new FakeRetriever();
        var sut = new MockCodeIndexServiceCompat(opts, fakeRetriever);

        var results = sut.Search("fhir-server", "anything");

        fakeRetriever.CallCount.Should().Be(1);
        results.Should().ContainSingle();
        results[0].FilePath.Should().Be("src/Real.cs");
        results[0].Snippet.Should().Be("real snippet");
    }

    [Fact]
    public void Search_FlagOn_NoRetriever_FallsBackToMock()
    {
        var opts = OptionsFactory.Create(new IndexingOptions { UseRealRetriever = true });
        var sut = new MockCodeIndexServiceCompat(opts, retriever: null);

        // Should not throw — falls back to mock base class
        var act = () => sut.Search("fhir-server", "something");
        act.Should().NotThrow();
    }
}
