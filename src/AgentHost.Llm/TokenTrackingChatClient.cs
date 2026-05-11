using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgentHost.Llm;

public sealed class TokenTrackingChatClient : DelegatingChatClient
{
    private readonly ITokenAccountant _accountant;
    private readonly string _agentId;
    private readonly string _provider;
    private readonly string _model;

    public TokenTrackingChatClient(IChatClient inner, ITokenAccountant accountant, string agentId, string provider, string model)
        : base(inner)
    {
        _accountant = accountant;
        _agentId = agentId;
        _provider = provider;
        _model = model;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        Record(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            updates.Add(update);
            yield return update;
        }

        var response = updates.Count > 0 ? updates.ToChatResponse() : null;
        var usage = response?.Usage ?? updates.Select(static update => update.RawRepresentation).OfType<UsageDetails>().LastOrDefault();
        Record(usage);
    }

    private void Record(UsageDetails? usage)
    {
        _accountant.Record(new TokenUsage(
            _agentId,
            _provider,
            _model,
            ToInt32(usage?.InputTokenCount),
            ToInt32(usage?.OutputTokenCount)));
    }

    private static int ToInt32(long? value)
        => value is null ? 0 : checked((int)value.Value);
}
