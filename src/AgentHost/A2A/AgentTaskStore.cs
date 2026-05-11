// This file is intentionally left minimal.
// AgentTaskStore has been superseded by IAgentTaskStore / InMemoryAgentTaskStore
// in AgentHost.Repositories.Tasks (Phase 4).
// The old concrete class is retained here only to avoid breaking any external
// callers that may reference the type by name during the transition period.

using AgentHost.Repositories.Tasks;

namespace AgentHost.A2A;

/// <summary>
/// Transitional shim — delegates to <see cref="InMemoryAgentTaskStore"/>.
/// Prefer injecting <see cref="IAgentTaskStore"/> directly.
/// </summary>
[Obsolete("Use IAgentTaskStore from AgentHost.Repositories.Tasks instead.")]
public sealed class AgentTaskStore
{
    // Intentionally empty — all callers now use IAgentTaskStore.
}
