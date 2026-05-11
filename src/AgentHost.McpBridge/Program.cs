using AgentHost.McpBridge;

var url = Environment.GetEnvironmentVariable("EXPERTS_MCP_URL") ?? "http://localhost:5000/mcp";

for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--url")
    {
        url = args[i + 1];
        break;
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var bridge = new StdioBridge(
    new HttpClient { BaseAddress = new Uri(url) },
    Console.In,
    Console.Out,
    Console.Error);

await bridge.RunAsync(cts.Token);
