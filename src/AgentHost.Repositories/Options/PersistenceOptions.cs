namespace AgentHost.Repositories.Options;

/// <summary>Top-level options for the Persistence subsystem.</summary>
public sealed class PersistenceOptions
{
    public const string Section = "Persistence";

    public TaskStoreOptions TaskStore { get; set; } = new();
    public MemoryOptions Memory { get; set; } = new();
}

/// <summary>Options for the agent task store backend.</summary>
public sealed class TaskStoreOptions
{
    /// <summary><c>InMemory</c> or <c>Postgres</c>.</summary>
    public string Backend { get; set; } = "InMemory";

    public TaskStorePostgresOptions Postgres { get; set; } = new();
}

public sealed class TaskStorePostgresOptions
{
    public string? ConnectionString { get; set; }
}

/// <summary>Options for the agent memory backend.</summary>
public sealed class MemoryOptions
{
    /// <summary><c>InMemory</c> or <c>Postgres+Qdrant</c>.</summary>
    public string Backend { get; set; } = "InMemory";

    public MemoryPostgresOptions Postgres { get; set; } = new();
    public MemoryQdrantOptions Qdrant { get; set; } = new();

    /// <summary>Default TTL for memory entries.</summary>
    public int DefaultRetentionDays { get; set; } = 30;

    /// <summary>Override the model used for summarisation (null = use agent default).</summary>
    public string? SummaryModelOverride { get; set; }

    /// <summary>How often the retention sweep runs, in minutes.</summary>
    public int SweepIntervalMinutes { get; set; } = 60;
}

public sealed class MemoryPostgresOptions
{
    public string? ConnectionString { get; set; }
}

public sealed class MemoryQdrantOptions
{
    public string Collection { get; set; } = "agent_memory";
}
