using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("AgentHost.Tests")]

namespace AgentHost.Repositories;

/// <summary>Marker type for assembly scanning. Will be populated in Phase 1+.</summary>
public static class AssemblyMarker { }
