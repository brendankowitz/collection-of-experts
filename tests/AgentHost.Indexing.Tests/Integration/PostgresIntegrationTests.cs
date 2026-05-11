using AgentHost.Indexing.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;
using AgentHost.Indexing.Options;

// Alias to avoid ambiguity with AgentHost.Indexing.Options namespace.
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace AgentHost.Indexing.Tests.Integration;

/// <summary>
/// Integration tests that require Docker.  Skipped in CI via
/// <c>--filter "Category!=Integration"</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithDatabase("experts")
            .WithUsername("experts")
            .WithPassword("experts")
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task Migration_CreatesTablesSuccessfully()
    {
        var opts = OptionsFactory.Create(new PostgresOptions
        {
            ConnectionString = GetContainer().GetConnectionString(),
        });

        var store = new PostgresMetadataStore(opts, NullLogger<PostgresMetadataStore>.Instance);
        await store.MigrateAsync();

        // Should not throw on second run (idempotent)
        await store.MigrateAsync();
    }

    [DockerFact]
    public async Task UpsertAndQuery_Repository()
    {
        var opts = OptionsFactory.Create(new PostgresOptions
        {
            ConnectionString = GetContainer().GetConnectionString(),
        });

        var store = new PostgresMetadataStore(opts, NullLogger<PostgresMetadataStore>.Instance);
        await store.MigrateAsync();

        var repo = new RepositoryRecord(
            "fhir-server", "FHIR Server", "https://github.com/microsoft/fhir-server",
            "main", null, DateTime.UtcNow, DateTime.UtcNow);

        await store.UpsertRepositoryAsync(repo);

        var retrieved = await store.GetRepositoryAsync("fhir-server");
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("FHIR Server");
    }

    [DockerFact]
    public async Task UpsertAndQuery_IndexedFile()
    {
        var opts = OptionsFactory.Create(new PostgresOptions
        {
            ConnectionString = GetContainer().GetConnectionString(),
        });

        var store = new PostgresMetadataStore(opts, NullLogger<PostgresMetadataStore>.Instance);
        await store.MigrateAsync();

        var repo = new RepositoryRecord(
            "hsc", "Healthcare Components", "https://github.com/microsoft/healthcare-shared-components",
            "main", null, DateTime.UtcNow, DateTime.UtcNow);
        await store.UpsertRepositoryAsync(repo);

        var file = new IndexedFileRecord(
            Guid.NewGuid(), "hsc", "src/Dicom/DicomService.cs", "abc123", DateTime.UtcNow);
        await store.UpsertIndexedFileAsync(file);

        var retrieved = await store.GetIndexedFileAsync("hsc", "src/Dicom/DicomService.cs");
        retrieved.Should().NotBeNull();
        retrieved!.ContentHash.Should().Be("abc123");
    }

    [DockerFact]
    public async Task IndexingJob_CreateAndUpdateStatus()
    {
        var opts = OptionsFactory.Create(new PostgresOptions
        {
            ConnectionString = GetContainer().GetConnectionString(),
        });

        var store = new PostgresMetadataStore(opts, NullLogger<PostgresMetadataStore>.Instance);
        await store.MigrateAsync();

        var repo = new RepositoryRecord(
            "job-repo", "Job Test Repo", "https://github.com/x/y",
            "main", null, DateTime.UtcNow, DateTime.UtcNow);
        await store.UpsertRepositoryAsync(repo);

        var jobId = Guid.NewGuid();
        await store.CreateJobAsync(new IndexingJobRecord(
            jobId, "job-repo", JobKind.Full, JobStatus.Pending,
            DateTime.UtcNow, null, null));

        var pending = await store.GetNextPendingJobAsync();
        pending.Should().NotBeNull();
        pending!.Id.Should().Be(jobId);

        await store.UpdateJobStatusAsync(jobId, JobStatus.Completed);

        var completed = await store.GetJobAsync(jobId);
        completed!.Status.Should().Be(JobStatus.Completed);
        completed.FinishedAt.Should().NotBeNull();
    }

    private PostgreSqlContainer GetContainer()
        => _container ?? throw new InvalidOperationException("Container has not been initialized.");
}
