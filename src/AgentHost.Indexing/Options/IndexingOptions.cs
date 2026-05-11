namespace AgentHost.Indexing.Options;

/// <summary>Top-level indexing configuration section.</summary>
public sealed class IndexingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string Section = "Indexing";

    /// <summary>When <c>true</c>, the real <see cref="Retrieval.HybridRetriever"/> is used instead of the mock.</summary>
    public bool UseRealRetriever { get; set; } = false;

    /// <summary>Local directory where repository clones are cached.</summary>
    public string RepoCacheDir { get; set; } = "./repo-cache";

    /// <summary>Files larger than this byte threshold are skipped during indexing.</summary>
    public long MaxFileSizeBytes { get; set; } = 1_048_576;

    /// <summary>Qdrant connection settings.</summary>
    public QdrantOptions Qdrant { get; set; } = new();

    /// <summary>Postgres connection settings.</summary>
    public PostgresOptions Postgres { get; set; } = new();

    /// <summary>Embedding provider settings.</summary>
    public EmbedderOptions Embedder { get; set; } = new();
}

/// <summary>Qdrant connection options.</summary>
public sealed class QdrantOptions
{
    /// <summary>Qdrant server hostname (default <c>localhost</c>).</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Qdrant gRPC port (default <c>6334</c>).</summary>
    public int GrpcPort { get; set; } = 6334;

    /// <summary>Optional API key.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>Postgres connection options.</summary>
public sealed class PostgresOptions
{
    /// <summary>Npgsql connection string.</summary>
    public string ConnectionString { get; set; } =
        "Host=localhost;Database=experts;Username=experts;Password=experts";
}

/// <summary>Embedding provider options.</summary>
public sealed class EmbedderOptions
{
    /// <summary>Provider: <c>Mock</c> | <c>Voyage</c> | <c>AzureOpenAI</c>.</summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>Model name override (provider-specific).</summary>
    public string? Model { get; set; }

    /// <summary>API key for external providers.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Azure OpenAI endpoint URL (AzureOpenAI provider only).</summary>
    public string? Endpoint { get; set; }
}
