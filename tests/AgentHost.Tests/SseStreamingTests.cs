using System.Net;
using System.Net.Http.Json;
using AgentHost;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentHost.Tests;

public sealed class SseStreamingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SseStreamingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:DefaultProvider"] = "Mock",
                    ["Llm:DefaultModel"] = "mock-model"
                });
            });
        });
    }

    [Fact]
    public async Task SendSubscribe_FhirAgent_StreamsDataEventsAndDoneEvent()
    {
        using var client = _factory.CreateClient();

        var request = new
        {
            agentId = "fhir-server-expert",
            sessionId = "test-sse-1",
            message = new
            {
                role = "user",
                parts = new[]
                {
                    new { type = "text", text = "Explain FHIR search architecture" }
                }
            }
        };

        var response = await client.PostAsJsonAsync("/tasks/sendSubscribe", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        var body = await response.Content.ReadAsStringAsync();
        var dataLines = body.Split('\n').Where(static line => line.StartsWith("data:", StringComparison.Ordinal)).ToList();

        dataLines.Count.Should().BeGreaterThanOrEqualTo(2);
        body.Should().Contain("\"done\"");
    }
}
