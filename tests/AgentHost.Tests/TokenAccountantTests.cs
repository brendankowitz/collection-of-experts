using AgentHost.Llm;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentHost.Tests;

public sealed class TokenAccountantTests
{
    [Fact]
    public void Record_Usage_IncreasesHistoryCount()
    {
        using var accountant = new TokenAccountant();

        accountant.Record(new TokenUsage("fhir-agent", "Mock", "gpt-4o", 100, 200));
        accountant.Record(new TokenUsage("fhir-agent", "Mock", "gpt-4o", 50, 100));

        var history = accountant.GetHistory();
        history.Count.Should().Be(2);
        history[0].InputTokens.Should().Be(100);
        history[1].OutputTokens.Should().Be(100);
        history[0].TotalTokens.Should().Be(300);
    }

    [Fact]
    public async Task MockChatClient_RecordsTokensViaFactory()
    {
        using var accountant = new TokenAccountant();
        using var mockClient = new MockChatClient("test-model");
        using var tracker = new TokenTrackingChatClient(mockClient, accountant, "test-agent", "Mock", "test-model");

        await tracker.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        accountant.GetHistory().Should().HaveCount(1);
    }

    [Fact]
    public async Task MockChatClient_Streaming_RecordsTokensViaFactory()
    {
        using var accountant = new TokenAccountant();
        using var mockClient = new MockChatClient("test-model");
        using var tracker = new TokenTrackingChatClient(mockClient, accountant, "test-agent", "Mock", "test-model");

        await foreach (var _ in tracker.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
        {
        }

        accountant.GetHistory().Should().HaveCount(1);
        accountant.GetHistory()[0].InputTokens.Should().BeGreaterThan(0);
        accountant.GetHistory()[0].OutputTokens.Should().BeGreaterThan(0);
    }
}
