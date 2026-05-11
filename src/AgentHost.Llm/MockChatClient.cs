using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentHost.Llm;

public sealed class MockChatClient : IChatClient
{
    private static readonly UsageDetails MockUsage = new()
    {
        InputTokenCount = 10,
        OutputTokenCount = 20,
        TotalTokenCount = 30
    };

    private readonly string _modelId;
    private readonly ChatClientMetadata _metadata;

    public MockChatClient(string modelId = "mock")
    {
        _modelId = modelId;
        _metadata = new ChatClientMetadata(providerName: "Mock", providerUri: null, defaultModelId: modelId);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        var text = BuildMockText(messageList);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = _modelId,
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails
            {
                InputTokenCount = MockUsage.InputTokenCount,
                OutputTokenCount = MockUsage.OutputTokenCount,
                TotalTokenCount = MockUsage.TotalTokenCount
            }
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var text = BuildMockText(messageList);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ")
            {
                ModelId = _modelId
            };
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, (string?)null)
        {
            ModelId = _modelId,
            FinishReason = ChatFinishReason.Stop,
            RawRepresentation = new UsageDetails
            {
                InputTokenCount = MockUsage.InputTokenCount,
                OutputTokenCount = MockUsage.OutputTokenCount,
                TotalTokenCount = MockUsage.TotalTokenCount
            }
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(ChatClientMetadata) ? _metadata : null;

    public void Dispose()
    {
    }

    private string BuildMockText(IList<ChatMessage> messages)
    {
        var lastUserMessage = messages.LastOrDefault(static m => m.Role == ChatRole.User)?.Text ?? "your query";
        return $"[MOCK] Mock response for: {lastUserMessage}. " +
               $"The Expert Agent system uses {_modelId} via Microsoft.Extensions.AI for real responses. " +
               "Configure a real provider in appsettings.json to get actual LLM responses.";
    }
}
