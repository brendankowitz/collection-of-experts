using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentHost.McpBridge;

/// <summary>
/// Bridges stdio JSON-RPC (line-delimited) to an AgentHost MCP HTTP endpoint.
/// Reads requests from stdin, forwards to POST {baseUrl}, writes responses to stdout.
/// For SSE responses, each data: line is forwarded as a JSON-RPC notification.
/// Logs to stderr because stdout is reserved for JSON-RPC messages.
/// </summary>
public sealed class StdioBridge(
    HttpClient httpClient,
    TextReader input,
    TextWriter output,
    TextWriter error)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await error.WriteLineAsync($"[experts-mcp] Bridge started. Forwarding to {httpClient.BaseAddress}");

        while (!ct.IsCancellationRequested)
        {
            var line = await ReadLineAsync(input, ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await error.WriteLineAsync("[experts-mcp] → " + line);

            try
            {
                await ForwardRequestAsync(line, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await error.WriteLineAsync("[experts-mcp] Error: " + ex.Message);
                var errorResponse = new
                {
                    jsonrpc = "2.0",
                    id = (object?)null,
                    error = new { code = -32603, message = ex.Message }
                };
                await WriteLineAsync(output, JsonSerializer.Serialize(errorResponse), ct);
            }
        }

        await error.WriteLineAsync("[experts-mcp] Bridge stopped.");
    }

    private async Task ForwardRequestAsync(string requestJson, CancellationToken ct)
    {
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await httpClient.PostAsync(string.Empty, content, ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await ForwardSseResponseAsync(requestJson, response, ct);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        await error.WriteLineAsync("[experts-mcp] ← " + body);
        await WriteLineAsync(output, body, ct);
    }

    private async Task ForwardSseResponseAsync(string originalRequest, HttpResponseMessage response, CancellationToken ct)
    {
        string? requestId = null;
        try
        {
            var parsed = JsonNode.Parse(originalRequest);
            requestId = parsed?["id"]?.ToString();
        }
        catch (JsonException)
        {
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await ReadLineAsync(reader, ct);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                await error.WriteLineAsync("[experts-mcp] SSE ← " + data);
                var notification = new
                {
                    jsonrpc = "2.0",
                    method = "notifications/message",
                    @params = new { id = requestId, data }
                };
                await WriteLineAsync(output, JsonSerializer.Serialize(notification), ct);
            }
        }
    }

    private static async Task<string?> ReadLineAsync(TextReader reader, CancellationToken ct)
    {
        try
        {
            return await reader.ReadLineAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task WriteLineAsync(TextWriter writer, string value, CancellationToken ct)
    {
        await writer.WriteLineAsync(value.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }
}
