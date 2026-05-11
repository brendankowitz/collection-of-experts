using AgentHost.A2A;
using A2ATaskStatus = AgentHost.A2A.TaskStatus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using AgentHost.Orchestration;
using AgentHost.Agents;

namespace AgentHost.Tests;

/// <summary>
/// Tests for depth-limit (test 5) and cycle-detection (test 6) in the A2A call context.
/// </summary>
public sealed class DepthLimitTests
{
    // ── Test 5: depth limit ───────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InProcessA2AClient_RejectsCallAtMaxDepth()
    {
        // Arrange: build a context already at depth = MaxCallDepth (5)
        var maxDepth = 5;
        var ctx = A2ACallContext.Empty;
        for (int i = 0; i < maxDepth; i++)
            ctx = ctx.Enter($"agent-{i}");

        using var _ = A2ACallContext.SetCurrent(ctx);

        var fakeAgent = new FakeExpertAgent("target-agent", "Target", "response");
        var registry = new AgentRegistry([fakeAgent], NullLogger<AgentRegistry>.Instance);
        var options = Options.Create(new OrchestrationOptions { MaxCallDepth = maxDepth });
        var client = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), options, NullLogger<InProcessA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "hello" }] }
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<A2ADepthExceededException>(
            () => client.SendTaskAsync(new Uri("inproc://target-agent"), req));

        ex.ErrorCode.Should().Be("A2A_DEPTH_EXCEEDED");
        ex.Depth.Should().Be(maxDepth);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InProcessA2AClient_Rejects6thCallInChain()
    {
        // Arrange: simulate a chain of 6 calls: agent-0 → agent-1 → ... → agent-5
        // The 6th call should fail because depth starts at 0 and MaxCallDepth = 5
        var maxDepth = 5;
        var ctx = A2ACallContext.Empty;
        for (int i = 0; i < maxDepth; i++)
            ctx = ctx.Enter($"agent-{i}");

        // ctx.Depth == 5 == maxDepth → next call should fail
        using var _ = A2ACallContext.SetCurrent(ctx);

        var options = Options.Create(new OrchestrationOptions { MaxCallDepth = maxDepth });
        var fakeAgent = new FakeExpertAgent("agent-5", "Agent5", "too deep");
        var registry = new AgentRegistry([fakeAgent], NullLogger<AgentRegistry>.Instance);
        var client = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), options, NullLogger<InProcessA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "x" }] }
        };

        await Assert.ThrowsAsync<A2ADepthExceededException>(
            () => client.SendTaskAsync(new Uri("inproc://agent-5"), req));
    }

    // ── Test 6: cycle detection ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InProcessA2AClient_RejectsCycleWhenAgentAlreadyInPath()
    {
        // Arrange: build a context where "coordinator" is already in the path
        var ctx = A2ACallContext.Empty.Enter("coordinator").Enter("fhir-server-expert");
        using var _ = A2ACallContext.SetCurrent(ctx);

        var fakeAgent = new FakeExpertAgent("coordinator", "Coordinator", "cycle!");
        var registry = new AgentRegistry([fakeAgent], NullLogger<AgentRegistry>.Instance);
        var options = Options.Create(new OrchestrationOptions());
        var client = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), options, NullLogger<InProcessA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "loop" }] }
        };

        // Act & Assert: calling coordinator again should fail with cycle detection
        var ex = await Assert.ThrowsAsync<A2ACycleDetectedException>(
            () => client.SendTaskAsync(new Uri("inproc://coordinator"), req));

        ex.ErrorCode.Should().Be("A2A_CYCLE_DETECTED");
        ex.AgentId.Should().Be("coordinator");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InProcessA2AClient_AllowsCallToNewAgentNotInPath()
    {
        // Arrange: path has agent-a, calling agent-b should succeed
        var ctx = A2ACallContext.Empty.Enter("agent-a");
        using var _ = A2ACallContext.SetCurrent(ctx);

        var fakeAgent = new FakeExpertAgent("agent-b", "Agent B", "ok");
        var registry = new AgentRegistry([fakeAgent], NullLogger<AgentRegistry>.Instance);
        var options = Options.Create(new OrchestrationOptions());
        var client = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), options, NullLogger<InProcessA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "allowed" }] }
        };

        // Should not throw
        var result = await client.SendTaskAsync(new Uri("inproc://agent-b"), req);
        result.Status.Should().Be(A2ATaskStatus.Completed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void A2ACallContext_Enter_IncrementsDepthAndAppendsPath()
    {
        var ctx = A2ACallContext.Empty;
        ctx.Depth.Should().Be(0);
        ctx.Path.Should().BeEmpty();

        var ctx2 = ctx.Enter("agent-a");
        ctx2.Depth.Should().Be(1);
        ctx2.Path.Should().ContainSingle("agent-a");

        var ctx3 = ctx2.Enter("agent-b");
        ctx3.Depth.Should().Be(2);
        ctx3.Path.Should().Equal("agent-a", "agent-b");

        // Original contexts are unchanged (immutable)
        ctx.Depth.Should().Be(0);
        ctx2.Depth.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void A2ACallContext_SetCurrent_RestoresOnDispose()
    {
        var original = A2ACallContext.Empty;
        A2ACallContext.SetCurrent(original);

        var newCtx = original.Enter("some-agent");
        using (A2ACallContext.SetCurrent(newCtx))
        {
            A2ACallContext.Current.Depth.Should().Be(1);
        }

        A2ACallContext.Current.Depth.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void A2ACallContext_FromHeaders_ParsesCorrectly()
    {
        var ctx = A2ACallContext.FromHeaders("trace123", "3", "agent-a,agent-b,agent-c");
        ctx.TraceId.Should().Be("trace123");
        ctx.Depth.Should().Be(3);
        ctx.Path.Should().Equal("agent-a", "agent-b", "agent-c");
    }
}
