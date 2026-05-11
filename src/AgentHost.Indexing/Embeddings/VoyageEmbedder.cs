using System.Net.Http.Json;
using System.Text.Json;
using AgentHost.Indexing.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHost.Indexing.Embeddings;

/// <summary>
/// Embedder backed by the Voyage AI REST API (<c>voyage-code-3</c> model).
/// </summary>
public sealed class VoyageEmbedder : IEmbedder
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<VoyageEmbedder> _logger;

    // voyage-code-3 produces 1024-dimensional vectors.
    private const int VoyageDimensions = 1024;

    /// <inheritdoc />
    public int Dimensions => VoyageDimensions;

    /// <summary>Creates the Voyage embedder.</summary>
    public VoyageEmbedder(HttpClient http, IOptions<EmbedderOptions> options, ILogger<VoyageEmbedder> logger)
    {
        _http = http;
        _model = options.Value.Model ?? "voyage-code-3";
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>[]> EmbedAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0) return [];

        var request = new { input = textList, model = _model, input_type = "query" };
        using var response = await _http.PostAsJsonAsync(
            "https://api.voyageai.com/v1/embeddings", request, ct);

        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct)
            ?? throw new InvalidOperationException("Empty response from Voyage AI");

        var data = doc.RootElement.GetProperty("data");
        var results = new ReadOnlyMemory<float>[textList.Count];

        foreach (var item in data.EnumerateArray())
        {
            int index = item.GetProperty("index").GetInt32();
            var embedding = item.GetProperty("embedding");
            var floats = new float[embedding.GetArrayLength()];
            int i = 0;
            foreach (var v in embedding.EnumerateArray())
                floats[i++] = v.GetSingle();
            results[index] = floats;
        }

        _logger.LogDebug("Voyage embedded {Count} texts", textList.Count);
        return results;
    }
}
