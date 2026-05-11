using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace AgentHost.Observability;

/// <summary>
/// Central registry for AgentHost OpenTelemetry ActivitySource and Meter.
/// </summary>
public static class AgentHostActivitySource
{
    public static readonly string Name = "AgentHost";

    public static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    public static readonly ActivitySource Source = new(Name, Version);
}

/// <summary>
/// Central registry for AgentHost OpenTelemetry Meter and instruments.
/// </summary>
public static class AgentHostMetrics
{
    public static readonly string MeterName = "AgentHost";

    private static readonly Meter _meter = new(MeterName, AgentHostActivitySource.Version);

    // Agent task metrics
    public static readonly Histogram<double> TaskDuration =
        _meter.CreateHistogram<double>("agent.task.duration", "ms", "Duration of an agent task end-to-end");

    public static readonly Counter<long> TaskCount =
        _meter.CreateCounter<long>("agent.task.count", "tasks", "Number of agent tasks processed");

    // LLM metrics (complementing TokenAccountant's Meter)
    public static readonly Histogram<double> LlmRequestDuration =
        _meter.CreateHistogram<double>("llm.request.duration", "ms", "Duration of an LLM completion request");

    public static readonly Histogram<double> LlmRequestCostUsd =
        _meter.CreateHistogram<double>("llm.request.cost.usd", "USD", "Estimated cost of an LLM request");

    // Retrieval metrics
    public static readonly Histogram<double> RetrievalDuration =
        _meter.CreateHistogram<double>("retrieval.duration", "ms", "Duration of a code retrieval query");

    public static readonly Counter<long> RetrievalHits =
        _meter.CreateCounter<long>("retrieval.hits", "results", "Number of retrieval results returned");

    // A2A metrics
    public static readonly Histogram<long> A2ACallDepth =
        _meter.CreateHistogram<long>("a2a.call.depth", "depth", "Depth of nested A2A calls");

    // MCP tool metrics
    public static readonly Histogram<double> McpToolCallDuration =
        _meter.CreateHistogram<double>("mcp.tool.call.duration", "ms", "Duration of an MCP tool call");
}
