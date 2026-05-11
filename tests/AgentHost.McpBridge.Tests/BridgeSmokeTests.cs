using System.Net;
using System.Text;
using AgentHost.McpBridge;
using FluentAssertions;
using Xunit;

namespace AgentHost.McpBridge.Tests;

public sealed class BridgeSmokeTests
{
    [Fact]
    public async Task RequestResponse_RoundTrip_WritesResponseToStdout()
    {
        const string request = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";
        const string expectedResponse = """{"jsonrpc":"2.0","id":1,"result":{"tools":[]}}""";

        var fakeHandler = new FakeHttpMessageHandler(expectedResponse, "application/json");
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://localhost:5000/mcp") };

        var stdin = new StringReader(request + Environment.NewLine);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var bridge = new StdioBridge(httpClient, stdin, stdout, stderr);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bridge.RunAsync(cts.Token);

        stdout.ToString().Trim().Should().Contain(expectedResponse);
    }

    [Fact]
    public async Task SseResponse_ForwardsDataLinesAsNotifications()
    {
        const string request = """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"ask_agent","arguments":{}}}""";
        const string sseBody = "event: message\ndata: hello world\n\ndata: [DONE]\n\n";

        var fakeHandler = new FakeHttpMessageHandler(sseBody, "text/event-stream");
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://localhost:5000/mcp") };

        var stdin = new StringReader(request + Environment.NewLine);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var bridge = new StdioBridge(httpClient, stdin, stdout, stderr);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await bridge.RunAsync(cts.Token);

        var output = stdout.ToString();
        output.Should().Contain("notifications/message");
        output.Should().Contain("hello world");
    }
}

internal sealed class FakeHttpMessageHandler(string body, string contentType) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
        return Task.FromResult(response);
    }
}
