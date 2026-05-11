using System.Net;
using System.Net.Http.Json;
using AgentHost;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgentHost.Tests;

public sealed class FhirAgentEndToEndTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FhirAgentEndToEndTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:DefaultProvider"] = "Mock",
                    ["Llm:DefaultModel"] = "test-model"
                });
            });
        });
    }

    [Fact]
    public async Task SendTask_FhirAgent_ReturnsMockLlmResponse()
    {
        using var client = _factory.CreateClient();

        var request = new
        {
            agentId = "fhir-server-expert",
            sessionId = "test-sync-1",
            message = new
            {
                role = "user",
                parts = new[]
                {
                    new { type = "text", text = "Where is FHIR search implemented?" }
                }
            }
        };

        var response = await client.PostAsJsonAsync("/tasks/send", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("[MOCK]");
        body.Should().ContainEquivalentOf("FHIR search");
    }
}
