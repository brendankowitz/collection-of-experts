using System.Net.Http.Headers;
using System.Text.Json;
using AgentHost;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentHost.Tests;

/// <summary>
/// Test 8: MCP ask_agent round-trip still works after the IA2AClient refactor.
/// </summary>
public sealed class AskAgentMcpToolTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static HttpRequestMessage JsonRpc(object payload)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return req;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            return body;

        return string.Concat(
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line[5..].Trim())
                .Where(line => !string.Equals(line, "[DONE]", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AskAgent_ReturnsResponseFromFhirExpert()
    {
        using var req = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 100,
            method = "tools/call",
            @params = new
            {
                name = "ask_agent",
                arguments = new
                {
                    agentId = "fhir-server-expert",
                    message = "What is FHIR?"
                }
            }
        });

        using var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var json = await ReadBodyAsync(response);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var content).Should().BeTrue();

        var text = content.EnumerateArray().First().GetProperty("text").GetString() ?? string.Empty;
        text.Should().NotBeEmpty();

        // The MCP tool should return a JSON object with agentId and response
        using var inner = JsonDocument.Parse(text);
        inner.RootElement.TryGetProperty("agentId", out var agentIdProp).Should().BeTrue();
        agentIdProp.GetString().Should().Be("fhir-server-expert");
        inner.RootElement.TryGetProperty("response", out var responseProp).Should().BeTrue();
        responseProp.GetString().Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task AskAgent_UnknownAgent_ReturnsError()
    {
        using var req = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 101,
            method = "tools/call",
            @params = new
            {
                name = "ask_agent",
                arguments = new
                {
                    agentId = "nonexistent-agent",
                    message = "hello"
                }
            }
        });

        using var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var json = await ReadBodyAsync(response);
        json.Should().Contain("error");
    }
}
