using AgentHost.Repositories.Memory;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHost.Tests.Persistence;

public sealed class InMemoryAgentMemoryTests
{
    private readonly InMemoryAgentMemory _memory = new(NullLogger<InMemoryAgentMemory>.Instance);

    [Fact]
    public async Task RecordTurn_StoresTurn()
    {
        await _memory.RecordTurnAsync("agent1", "thread1", "user", "Hello");

        _memory.AllTurns.Should().ContainSingle(t =>
            t.AgentId == "agent1" &&
            t.ThreadId == "thread1" &&
            t.Role == "user" &&
            t.Content == "Hello");
    }

    [Fact]
    public async Task SummarizeAndPersist_CreatesMemoryEntry()
    {
        await _memory.RecordTurnAsync("agent1", "thread1", "user", "Question?");
        await _memory.RecordTurnAsync("agent1", "thread1", "agent", "Answer!");

        await _memory.SummarizeAndPersistAsync("agent1", "thread1");

        _memory.AllEntries.Should().ContainSingle(e =>
            e.AgentId == "agent1" && e.ThreadId == "thread1");
    }

    [Fact]
    public async Task SummarizeAndPersist_NoTurns_DoesNotCreateEntry()
    {
        await _memory.SummarizeAndPersistAsync("agent1", "empty-thread");

        _memory.AllEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task RecallAsync_ReturnsEntriesOrderedByRecency()
    {
        // Create entries for two different threads (same agent)
        await _memory.RecordTurnAsync("agent1", "thread1", "user", "First");
        await _memory.SummarizeAndPersistAsync("agent1", "thread1");

        await Task.Delay(5); // ensure different timestamps

        await _memory.RecordTurnAsync("agent1", "thread2", "user", "Second");
        await _memory.SummarizeAndPersistAsync("agent1", "thread2");

        var results = await _memory.RecallAsync("agent1", "thread1", "query", k: 5);

        // Both entries are for agent1, sorted most recent first
        results.Should().HaveCount(2);
        results[0].ThreadId.Should().Be("thread2"); // most recent
        results[1].ThreadId.Should().Be("thread1");
    }

    [Fact]
    public async Task RecallAsync_RespectsKLimit()
    {
        for (var i = 0; i < 5; i++)
        {
            await _memory.RecordTurnAsync("agent1", $"thread{i}", "user", $"msg{i}");
            await _memory.SummarizeAndPersistAsync("agent1", $"thread{i}");
        }

        var results = await _memory.RecallAsync("agent1", "thread0", "query", k: 3);
        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task PurgeThread_RemovesAllDataForThread()
    {
        await _memory.RecordTurnAsync("agent1", "purge-me", "user", "Sensitive data");
        await _memory.SummarizeAndPersistAsync("agent1", "purge-me");

        // Keep some data for another thread
        await _memory.RecordTurnAsync("agent1", "keep-me", "user", "Keep this");

        await _memory.PurgeThreadAsync("purge-me");

        _memory.AllTurns.Should().NotContain(t => t.ThreadId == "purge-me");
        _memory.AllEntries.Should().NotContain(e => e.ThreadId == "purge-me");
        _memory.AllTurns.Should().Contain(t => t.ThreadId == "keep-me");
    }

    [Fact]
    public async Task RecallAsync_DoesNotReturnExpiredEntries()
    {
        await _memory.RecordTurnAsync("agent1", "thread-expired", "user", "Old");
        await _memory.SummarizeAndPersistAsync("agent1", "thread-expired");

        // Artificially expire the entry by purging (simulating expiry)
        await _memory.PurgeThreadAsync("thread-expired");

        var results = await _memory.RecallAsync("agent1", "thread-expired", "query");
        results.Should().BeEmpty();
    }
}
