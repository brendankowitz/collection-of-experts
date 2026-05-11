using System.Net;
using System.Text.Json;
using AgentHost;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentHost.Tests;

public sealed class SmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_Returns200()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AgentCard_FhirServerExpert_Returns200WithAgentId()
    {
        var response = await _client.GetAsync("/.well-known/agent-card.json?agentId=fhir-server-expert");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("agentId", out var agentIdProp).Should().BeTrue();
        agentIdProp.GetString().Should().Be("fhir-server-expert");
    }

    [Fact]
    public async Task ApiInfo_Returns200WithExpectedName()
    {
        var response = await _client.GetAsync("/api/info");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Expert Agents API");
    }
}
