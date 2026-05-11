using System.Diagnostics;

namespace AgentHost.A2A;

/// <summary>
/// Carries trace ID, call depth, and call path for cross-agent A2A calls.
/// Propagated via <see cref="AsyncLocal{T}"/> for in-process calls
/// and via HTTP headers (X-Trace-Id, X-Call-Depth, X-Call-Path) for remote calls.
/// </summary>
public sealed class A2ACallContext
{
    private static readonly AsyncLocal<A2ACallContext?> _current = new();

    public static A2ACallContext Empty { get; } = new();

    /// <summary>Gets or sets the active context for the current async execution flow.</summary>
    public static A2ACallContext Current
    {
        get => _current.Value ?? Empty;
        private set => _current.Value = value;
    }

    /// <summary>W3C-compatible trace ID; new on first entry, propagated thereafter.</summary>
    public string TraceId { get; init; } =
        Activity.Current?.TraceId.ToString() is { Length: > 0 } t ? t : Guid.NewGuid().ToString("N");

    /// <summary>Number of cross-agent hops made so far in this call chain.</summary>
    public int Depth { get; init; }

    /// <summary>Ordered list of agent IDs visited during this call chain.</summary>
    public IReadOnlyList<string> Path { get; init; } = [];

    /// <summary>Returns a new context with depth+1 and the given agent appended to the path.</summary>
    public A2ACallContext Enter(string agentId) => new()
    {
        TraceId = TraceId,
        Depth = Depth + 1,
        Path = [.. Path, agentId]
    };

    /// <summary>
    /// Sets <paramref name="ctx"/> as the current context for this async flow.
    /// Returns a disposable that restores the previous context when disposed.
    /// </summary>
    public static IDisposable SetCurrent(A2ACallContext ctx)
    {
        var prev = _current.Value;
        _current.Value = ctx;
        return new ContextRestorer(prev);
    }

    /// <summary>Parses a context from the A2A HTTP headers.</summary>
    public static A2ACallContext FromHeaders(string? traceId, string? depth, string? path)
    {
        return new A2ACallContext
        {
            TraceId = string.IsNullOrEmpty(traceId) ? Guid.NewGuid().ToString("N") : traceId,
            Depth = int.TryParse(depth, out var d) ? d : 0,
            Path = string.IsNullOrEmpty(path)
                ? []
                : path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }

    /// <summary>Adds this context's trace/depth/path to the given HTTP request headers.</summary>
    public void AddToHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Trace-Id", TraceId);
        request.Headers.TryAddWithoutValidation("X-Call-Depth", Depth.ToString());
        request.Headers.TryAddWithoutValidation("X-Call-Path", string.Join(',', Path));
    }

    private sealed class ContextRestorer(A2ACallContext? prev) : IDisposable
    {
        public void Dispose() => _current.Value = prev;
    }
}
