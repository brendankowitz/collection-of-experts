using AgentHost.Repositories.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dapper;
using Npgsql;

namespace AgentHost.Repositories.Memory;

/// <summary>
/// Background service that periodically sweeps expired memory entries from Postgres
/// and removes their corresponding vectors from Qdrant via <see cref="IAgentMemory.PurgeThreadAsync"/>.
/// Only registered when the Memory backend is <c>Postgres+Qdrant</c>.
/// </summary>
public sealed class MemoryRetentionService : BackgroundService
{
    private readonly PersistenceOptions _options;
    private readonly PostgresQdrantAgentMemory _memory;
    private readonly ILogger<MemoryRetentionService> _logger;

    public MemoryRetentionService(
        IOptions<PersistenceOptions> options,
        PostgresQdrantAgentMemory memory,
        ILogger<MemoryRetentionService> logger)
    {
        _options = options.Value;
        _memory = memory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.Memory.SweepIntervalMinutes);
        _logger.LogInformation("MemoryRetentionService starting, sweep interval {Interval}", interval);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Memory retention sweep started");

            var connectionString = _options.Memory.Postgres.ConnectionString;
            if (string.IsNullOrEmpty(connectionString)) return;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Find expired thread IDs
            var expiredThreads = (await conn.QueryAsync<string>(
                "SELECT DISTINCT thread_id FROM agent_memory WHERE expires_at <= NOW()")).ToList();

            if (expiredThreads.Count == 0)
            {
                _logger.LogInformation("Memory retention sweep: no expired entries");
                return;
            }

            foreach (var threadId in expiredThreads)
            {
                await _memory.PurgeThreadAsync(threadId, ct);
                _logger.LogInformation("Purged expired memory for thread {ThreadId}", threadId);
            }

            _logger.LogInformation("Memory retention sweep completed, purged {Count} thread(s)", expiredThreads.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory retention sweep failed");
        }
    }
}
