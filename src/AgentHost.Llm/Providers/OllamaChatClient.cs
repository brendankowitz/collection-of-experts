using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AgentHost.Llm.Providers;

internal sealed class OllamaChatClient : IChatClient
{
    private readonly OllamaApiClient _client;
    private readonly IChatClient _inner;
    private readonly string _model;
    private readonly ChatClientMetadata _metadata;

    public OllamaChatClient(string endpoint, string model)
    {
        _model = model;
        _client = new OllamaApiClient(new Uri(endpoint), model);
        _inner = (IChatClient)_client;
        _metadata = new ChatClientMetadata(providerName: "Ollama", providerUri: new Uri(endpoint), defaultModelId: model);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetResponseAsync(messages, WithModel(options), cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetStreamingResponseAsync(messages, WithModel(options), cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(ChatClientMetadata) ? _metadata : _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _client.Dispose();

    private ChatOptions WithModel(ChatOptions? options)
    {
        var configured = options?.Clone() ?? new ChatOptions();
        configured.ModelId ??= _model;
        return configured;
    }
}
