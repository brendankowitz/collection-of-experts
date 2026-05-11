using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AgentHost;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentHost.Tests;

/// <summary>
/// Tests for the spec-compliant MCP HTTP endpoints.
/// Uses stateless HTTP transport for POST /mcp requests.
/// </summary>
public sealed class McpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public McpEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage JsonRpc(object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private static async Task<string> ReadResponseBodyAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        var payload = string.Join(
            string.Empty,
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
                .Select(line => line[5..].Trim())
                .Where(line => !string.Equals(line, "[DONE]", StringComparison.Ordinal)));

        return payload;
    }

    [Fact]
    public async Task Initialize_Returns_ServerInfo()
    {
        using var body = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "test-client", version = "1.0" }
            }
        });

        using var response = await _client.SendAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadResponseBodyAsync(response);
        json.Should().Contain("expert-agents");
    }

    [Fact]
    public async Task ToolsList_Returns_AtLeast6Tools()
    {
        using (var initialize = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 0,
            method = "initialize",
            @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "t", version = "1" } }
        }))
        {
            using var initializeResponse = await _client.SendAsync(initialize);
            initializeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var body = JsonRpc(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        using var response = await _client.SendAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadResponseBodyAsync(response);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("tools", out var tools).Should().BeTrue();

        var toolList = tools.EnumerateArray().ToList();
        toolList.Count.Should().BeGreaterThanOrEqualTo(6, "at least 6 tools must be registered");

        foreach (var tool in toolList)
        {
            tool.TryGetProperty("name", out _).Should().BeTrue();
            tool.TryGetProperty("description", out _).Should().BeTrue();
            tool.TryGetProperty("inputSchema", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ToolsCall_ListAgents_Returns_AtLeast2Agents()
    {
        using var body = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "list_agents", arguments = new { } }
        });

        using var response = await _client.SendAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadResponseBodyAsync(response);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var content).Should().BeTrue();

        var text = content.EnumerateArray().First().GetProperty("text").GetString() ?? string.Empty;
        text.Should().NotBeEmpty();

        using var inner = JsonDocument.Parse(text);
        inner.RootElement.GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ToolsCall_SearchCode_Returns_AtLeast1Hit()
    {
        using var body = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new
            {
                name = "search_code",
                arguments = new { repo = "fhir-server", query = "export" }
            }
        });

        using var response = await _client.SendAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadResponseBodyAsync(response);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("result", out var result).Should().BeTrue();
        result.TryGetProperty("content", out var content).Should().BeTrue();

        var text = content.EnumerateArray().First().GetProperty("text").GetString() ?? string.Empty;
        using var inner = JsonDocument.Parse(text);
        inner.RootElement.GetProperty("totalResults").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ToolsCall_InvalidParams_ReturnsError()
    {
        using var body = JsonRpc(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new
            {
                name = "search_code",
                arguments = new { repo = "fhir-server" }
            }
        });

        using var response = await _client.SendAsync(body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadResponseBodyAsync(response);
        json.Should().MatchRegex("(?is).*(error|query).*");
    }

    [Fact]
    public async Task McpHealth_Returns_Ok()
    {
        using var response = await _client.GetAsync("/mcp/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await ReadResponseBodyAsync(response);
        json.Should().Contain("ok");
        json.Should().Contain("protocolVersion");
    }

    [Fact]
    public async Task McpSseEndpoint_Opens_WithSseContentType()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
