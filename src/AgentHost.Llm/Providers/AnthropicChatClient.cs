using Anthropic.SDK;
using Microsoft.Extensions.AI;

namespace AgentHost.Llm.Providers;

internal sealed class AnthropicChatClient : IChatClient
{
    private readonly AnthropicClient _client;
    private readonly IChatClient _inner;
    private readonly string _model;
    private readonly ChatClientMetadata _metadata;

    public AnthropicChatClient(string apiKey, string model)
    {
        _model = model;
        _client = new AnthropicClient(new APIAuthentication(apiKey));
        _inner = (IChatClient)_client.Messages;
        _metadata = new ChatClientMetadata(providerName: "Anthropic", providerUri: null, defaultModelId: model);
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
