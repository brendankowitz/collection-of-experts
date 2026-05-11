using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentHost.Llm;

public sealed class TokenAccountant : ITokenAccountant, IDisposable
{
    private readonly ConcurrentQueue<TokenUsage> _history = new();
    private readonly Meter _meter;
    private readonly Counter<long> _inputTokenCounter;
    private readonly Counter<long> _outputTokenCounter;
    private readonly Counter<long> _requestCounter;

    public TokenAccountant()
    {
        _meter = new Meter("AgentHost.Llm");
        _inputTokenCounter = _meter.CreateCounter<long>("llm.tokens.input", "tokens", "Number of input tokens consumed");
        _outputTokenCounter = _meter.CreateCounter<long>("llm.tokens.output", "tokens", "Number of output tokens produced");
        _requestCounter = _meter.CreateCounter<long>("llm.requests", "requests", "Number of LLM requests made");
    }

    public void Record(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        _history.Enqueue(usage);
        var tags = new TagList
        {
            { "agent", usage.AgentId },
            { "provider", usage.Provider },
            { "model", usage.Model }
        };

        _inputTokenCounter.Add(usage.InputTokens, tags);
        _outputTokenCounter.Add(usage.OutputTokens, tags);
        _requestCounter.Add(1, tags);
    }

    public IReadOnlyList<TokenUsage> GetHistory() => _history.ToArray();

    public void Dispose() => _meter.Dispose();
}
