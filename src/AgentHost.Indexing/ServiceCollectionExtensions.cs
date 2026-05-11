using AgentHost.Indexing.Chunking;
using AgentHost.Indexing.Embeddings;
using AgentHost.Indexing.Indexer;
using AgentHost.Indexing.Options;
using AgentHost.Indexing.Retrieval;
using AgentHost.Indexing.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentHost.Indexing;

/// <summary>
/// Extension methods for registering all AgentHost.Indexing services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the full code-indexing pipeline (storage, chunking, embeddings,
    /// indexer worker, and retriever) from the <c>Indexing</c> configuration section.
    /// </summary>
    public static IServiceCollection AddAgentHostIndexing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Options ───────────────────────────────────────────────────────────
        services.Configure<IndexingOptions>(configuration.GetSection(IndexingOptions.Section));
        services.Configure<QdrantOptions>(
            configuration.GetSection($"{IndexingOptions.Section}:Qdrant"));
        services.Configure<PostgresOptions>(
            configuration.GetSection($"{IndexingOptions.Section}:Postgres"));
        services.Configure<EmbedderOptions>(
            configuration.GetSection($"{IndexingOptions.Section}:Embedder"));

        // ── Storage ───────────────────────────────────────────────────────────
        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        services.AddSingleton<IMetadataStore, PostgresMetadataStore>();

        // ── Chunking ──────────────────────────────────────────────────────────
        services.AddSingleton<TreeSitterChunker>();
        services.AddSingleton<LineWindowChunker>();
        services.AddSingleton<ChunkerSelector>();

        // ── Embedder ──────────────────────────────────────────────────────────
        services.AddHttpClient<VoyageEmbedder>((sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmbedderOptions>>().Value;
            client.BaseAddress = new Uri("https://api.voyageai.com");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            if (!string.IsNullOrEmpty(opts.ApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
        });

        services.AddSingleton<IEmbedder>(sp =>
        {
            var opts = configuration
                .GetSection($"{IndexingOptions.Section}:Embedder")
                .Get<EmbedderOptions>() ?? new EmbedderOptions();

            return opts.Provider.ToUpperInvariant() switch
            {
                "VOYAGE" => sp.GetRequiredService<VoyageEmbedder>(),
                "AZUREOPENAI" => ActivatorUtilities.CreateInstance<AzureOpenAIEmbedder>(sp),
                _ => new MockEmbedder()
            };
        });

        // ── Indexer ───────────────────────────────────────────────────────────
        services.AddSingleton<IRepositoryFetcher, GitRepositoryFetcher>();
        services.AddHostedService<IndexingWorker>();

        // ── Retrieval ─────────────────────────────────────────────────────────
        services.AddSingleton<IReranker, NoopReranker>();
        services.AddSingleton<IRetriever, HybridRetriever>();

        return services;
    }
}
