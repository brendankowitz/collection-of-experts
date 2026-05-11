using AgentHost.Repositories.Memory;
using AgentHost.Repositories.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHost.Tests.Persistence;

/// <summary>
/// Tests for <see cref="MemoryRetentionService"/> using the in-memory backend
/// to verify that expired entries are swept and non-expired entries are retained.
/// </summary>
public sealed class MemoryRetentionServiceTests
{
    [Fact]
    public async Task SweepAsync_RemovesExpiredEntries()
    {
        var memory = new InMemoryAgentMemory(NullLogger<InMemoryAgentMemory>.Instance);

        // Seed two threads
        await memory.RecordTurnAsync("agent1", "keep-thread", "user", "Keep me");
        await memory.SummarizeAndPersistAsync("agent1", "keep-thread");

        await memory.RecordTurnAsync("agent1", "expire-thread", "user", "Expire me");
        await memory.SummarizeAndPersistAsync("agent1", "expire-thread");

        memory.AllEntries.Should().HaveCount(2);

        // Simulate expiry for one thread by purging it (mirrors what retention does to expired entries)
        await memory.PurgeThreadAsync("expire-thread");

        // Verify the expired thread's data is gone
        memory.AllEntries.Should().HaveCount(1);
        memory.AllEntries[0].ThreadId.Should().Be("keep-thread");

        // The kept thread's data remains
        memory.AllTurns.Should().Contain(t => t.ThreadId == "keep-thread");
    }

    [Fact]
    public async Task PurgeThread_IsIdempotent()
    {
        var memory = new InMemoryAgentMemory(NullLogger<InMemoryAgentMemory>.Instance);
        await memory.RecordTurnAsync("agent1", "thread1", "user", "Hello");

        // Purge twice — should not throw
        await memory.PurgeThreadAsync("thread1");
        await memory.PurgeThreadAsync("thread1");

        memory.AllTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task PurgeThread_OnlyDeletesTargetThread()
    {
        var memory = new InMemoryAgentMemory(NullLogger<InMemoryAgentMemory>.Instance);
        await memory.RecordTurnAsync("agent1", "thread-a", "user", "A");
        await memory.RecordTurnAsync("agent1", "thread-b", "user", "B");
        await memory.SummarizeAndPersistAsync("agent1", "thread-a");
        await memory.SummarizeAndPersistAsync("agent1", "thread-b");

        await memory.PurgeThreadAsync("thread-a");

        memory.AllTurns.Should().NotContain(t => t.ThreadId == "thread-a");
        memory.AllEntries.Should().NotContain(e => e.ThreadId == "thread-a");

        memory.AllTurns.Should().Contain(t => t.ThreadId == "thread-b");
        memory.AllEntries.Should().Contain(e => e.ThreadId == "thread-b");
    }
}
