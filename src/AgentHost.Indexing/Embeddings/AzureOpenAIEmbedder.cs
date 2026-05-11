using AgentHost.Indexing.Options;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace AgentHost.Indexing.Embeddings;

/// <summary>
/// Embedder backed by Azure OpenAI (default model <c>text-embedding-3-large</c>).
/// </summary>
public sealed class AzureOpenAIEmbedder : IEmbedder
{
    private readonly EmbeddingClient _client;
    private readonly string _model;
    private readonly ILogger<AzureOpenAIEmbedder> _logger;

    // text-embedding-3-large produces 3072-dimensional vectors.
    private const int DefaultDimensions = 3072;
    private readonly int _dimensions;

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <summary>Creates the Azure OpenAI embedder.</summary>
    public AzureOpenAIEmbedder(IOptions<EmbedderOptions> options, ILogger<AzureOpenAIEmbedder> logger)
    {
        var opt = options.Value;
        _model = opt.Model ?? "text-embedding-3-large";
        _dimensions = DefaultDimensions;
        _logger = logger;

        var endpoint = new Uri(opt.Endpoint ?? throw new InvalidOperationException("Indexing:Embedder:Endpoint is required for AzureOpenAI provider"));
        var credential = new AzureKeyCredential(opt.ApiKey ?? throw new InvalidOperationException("Indexing:Embedder:ApiKey is required for AzureOpenAI provider"));
        var aoai = new AzureOpenAIClient(endpoint, credential);
        _client = aoai.GetEmbeddingClient(_model);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>[]> EmbedAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var textList = texts.ToList();
        if (textList.Count == 0) return [];

        var response = await _client.GenerateEmbeddingsAsync(textList, cancellationToken: ct);
        var results = new ReadOnlyMemory<float>[textList.Count];

        for (int i = 0; i < response.Value.Count; i++)
            results[i] = response.Value[i].ToFloats();

        _logger.LogDebug("Azure OpenAI embedded {Count} texts using {Model}", textList.Count, _model);
        return results;
    }
}
