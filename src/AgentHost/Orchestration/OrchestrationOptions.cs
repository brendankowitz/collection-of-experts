namespace AgentHost.Orchestration;

/// <summary>Top-level orchestration configuration bound from <c>Orchestration</c> in appsettings.json.</summary>
public sealed class OrchestrationOptions
{
    public const string SectionName = "Orchestration";

    /// <summary>When true, the coordinator agent is registered and used for unrouted requests.</summary>
    public bool EnableCoordinator { get; set; } = true;

    /// <summary>Default routing mode: "coordinator" (LLM-based) or "direct" (legacy keyword scoring).</summary>
    public string DefaultRoutingMode { get; set; } = "coordinator";

    /// <summary>Coordinator-specific settings.</summary>
    public CoordinatorOptions Coordinator { get; set; } = new();

    /// <summary>Maximum number of cross-agent hops before a request is rejected with A2A_DEPTH_EXCEEDED.</summary>
    public int MaxCallDepth { get; set; } = 5;

    /// <summary>
    /// When true, calls to agents whose base URL matches localhost (or uses scheme <c>inproc://</c>)
    /// bypass HTTP and are dispatched directly via the in-process A2A client.
    /// </summary>
    public bool InProcessCallsForLocalAgents { get; set; } = true;
}

/// <summary>Options specific to the coordinator agent.</summary>
public sealed class CoordinatorOptions
{
    /// <summary>Maximum number of sub-agents to call in parallel per decomposition.</summary>
    public int MaxParallelAgents { get; set; } = 4;

    /// <summary>
    /// When true, after collecting all sub-agent responses the coordinator makes a final LLM call
    /// that merges them into a single synthesised answer.
    /// </summary>
    public bool EnableSynthesis { get; set; } = true;

    /// <summary>Optional model override for the synthesis step; null uses the default model.</summary>
    public string? SynthesisModelOverride { get; set; }
}
