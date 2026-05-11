using AgentHost.Repositories.Memory;
using AgentHost.Repositories.Options;
using AgentHost.Repositories.Registry;
using AgentHost.Repositories.Sources;
using AgentHost.Repositories.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentHost.Repositories;

/// <summary>
/// Extension methods for registering all persistence and registry services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAgentTaskStore"/> and <see cref="IAgentMemory"/> per the
    /// <c>Persistence</c> configuration section, and optionally <see cref="MemoryRetentionService"/>.
    /// </summary>
    public static IServiceCollection AddAgentHostPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.Section));

        var opts = configuration.GetSection(PersistenceOptions.Section).Get<PersistenceOptions>()
                   ?? new PersistenceOptions();

        // ── Task store ────────────────────────────────────────────────────────
        if (opts.TaskStore.Backend.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAgentTaskStore>(sp =>
            {
                var connectionString = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value
                    .TaskStore.Postgres.ConnectionString
                    ?? throw new InvalidOperationException(
                        "Persistence:TaskStore:Postgres:ConnectionString is required when backend is Postgres");

                var store = new PostgresAgentTaskStore(
                    connectionString,
                    sp.GetRequiredService<ILogger<PostgresAgentTaskStore>>());

                store.MigrateAsync().GetAwaiter().GetResult();
                return store;
            });
        }
        else
        {
            services.AddSingleton<IAgentTaskStore, InMemoryAgentTaskStore>();
        }

        // ── Agent memory ──────────────────────────────────────────────────────
        if (opts.Memory.Backend.Equals("Postgres+Qdrant", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<PostgresQdrantAgentMemory>();
            services.AddSingleton<IAgentMemory>(sp =>
            {
                var mem = sp.GetRequiredService<PostgresQdrantAgentMemory>();
                mem.MigrateAsync().GetAwaiter().GetResult();
                return mem;
            });
            services.AddHostedService<MemoryRetentionService>();
        }
        else
        {
            services.AddSingleton<IAgentMemory, InMemoryAgentMemory>();
        }

        return services;
    }

    /// <summary>
    /// Registers <see cref="IRepositoryRegistry"/> and repository source adapters per the
    /// <c>Repositories</c> configuration section.
    /// </summary>
    public static IServiceCollection AddRepositoryRegistry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RepositoriesOptions>(configuration.GetSection(RepositoriesOptions.Section));

        var backend = configuration[$"{RepositoriesOptions.Section}:Backend"] ?? "InMemory";

        if (backend.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<PostgresRepositoryRegistry>();
            services.AddSingleton<IRepositoryRegistry>(sp =>
            {
                var reg = sp.GetRequiredService<PostgresRepositoryRegistry>();
                return reg;
            });
        }
        else
        {
            services.AddSingleton<InMemoryRepositoryRegistry>();
            services.AddSingleton<IRepositoryRegistry>(sp => sp.GetRequiredService<InMemoryRepositoryRegistry>());
        }

        services.AddSingleton<GitHubRepositorySource>();
        services.AddHttpClient<AzureDevOpsRepositorySource>();
        services.AddSingleton<RepositorySourceFactory>();

        return services;
    }
}