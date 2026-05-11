using System.Diagnostics;
using AgentHost;
using AgentHost.Llm;
using AgentHost.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace AgentHost.Tests;

/// <summary>Phase 8 observability tests.</summary>
public sealed class ObservabilityTests
{
    // ── A. AddAgentHostObservability boots cleanly ────────────────────────────

    [Fact]
    public void AddAgentHostObservability_WithOtelEnabled_StartsCleanly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Otel:Enabled"] = "true",
                ["Observability:Otel:ServiceName"] = "test-agenthost",
                ["Observability:Otel:Exporter"] = "Console",
                ["Observability:Otel:ConsoleExporter"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ex = Record.Exception(() => services.AddAgentHostObservability(config));
        ex.Should().BeNull();

        using var provider = services.BuildServiceProvider();
        ex = Record.Exception(() => provider.GetService<object>());
        ex.Should().BeNull();
    }

    [Fact]
    public void AddAgentHostObservability_WithOtelDisabled_StartsCleanly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:Otel:Enabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ex = Record.Exception(() => services.AddAgentHostObservability(config));
        ex.Should().BeNull();
    }

    // ── B. Activity created for A2A call has expected tags ────────────────────

    [Fact]
    public void ActivitySource_StartActivity_HasExpectedTags()
    {
        var activities = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentHostActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = act => activities.Add(act),
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = AgentHostActivitySource.Source.StartActivity("a2a.task.send");
        activity?.SetTag("agent.id", "test-agent");
        activity?.SetTag("tool.name", "code_search");

        activity.Should().NotBeNull();
        activity!.GetTagItem("agent.id").Should().Be("test-agent");
        activity.GetTagItem("tool.name").Should().Be("code_search");
    }

    // ── C. Token metric counter increments on LLM call ────────────────────────

    [Fact]
    public async Task TokenAccountant_MetricCounterIncrements_OnLlmCall()
    {
        using var accountant = new TokenAccountant();
        using var mockClient = new MockChatClient("gpt-4o");
        using var tracker = new TokenTrackingChatClient(mockClient, accountant, "test-agent", "Mock", "gpt-4o");

        var before = accountant.GetHistory().Count;
        await tracker.GetResponseAsync([new ChatMessage(ChatRole.User, "What is FHIR?")]);
        var after = accountant.GetHistory().Count;

        after.Should().BeGreaterThan(before);
        accountant.GetHistory().Last().AgentId.Should().Be("test-agent");
        accountant.GetHistory().Last().InputTokens.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── D. Admin usage endpoint returns 200 ───────────────────────────────────

    [Fact]
    public async Task AdminUsage_Endpoint_Returns200()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // Auth is disabled by default (appsettings.json Authentication:Mode = Disabled)
        var response = await client.GetAsync("/admin/usage");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("totalRequests");
        body.Should().Contain("breakdown");
    }
}
