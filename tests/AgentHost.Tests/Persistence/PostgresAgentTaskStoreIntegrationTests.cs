using AgentHost.Repositories.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHost.Tests.Persistence;

/// <summary>
/// Integration tests for <see cref="PostgresAgentTaskStore"/>.
/// These require a real Postgres instance and are skipped in default CI runs.
/// Run with: dotnet test --filter "Category=Integration"
/// Requires: Testcontainers.PostgreSql NuGet package (add if running integration tests).
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresAgentTaskStoreIntegrationTests
{
    // NOTE: To run these tests, add Testcontainers.PostgreSql to the test project
    // and uncomment the Testcontainers setup code below. By default these tests are
    // skipped from CI via --filter "Category!=Integration".
    //
    // Example setup (with Testcontainers):
    //
    // private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
    //     .WithDatabase("test")
    //     .WithUsername("test")
    //     .WithPassword("test")
    //     .Build();
    //
    // public async Task InitializeAsync() => await _postgres.StartAsync();
    // public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static string GetConnectionString()
    {
        // Override via environment variable for local integration test runs
        return Environment.GetEnvironmentVariable("INTEGRATION_POSTGRES_CS")
               ?? "Host=localhost;Database=experts_test;Username=experts;Password=experts";
    }

    [Fact(Skip = "Requires a live Postgres instance. Set INTEGRATION_POSTGRES_CS env var to enable.")]
    public async Task RoundTrip_CreateGetComplete()
    {
        var cs = GetConnectionString();
        var store = new PostgresAgentTaskStore(cs, NullLogger<PostgresAgentTaskStore>.Instance);
        await store.MigrateAsync();

        // Create
        var task = await store.CreateTaskAsync("session1", "agent1", "user", "Hello");
        task.Id.Should().NotBeNullOrEmpty();
        task.State.Should().Be(TaskState.Submitted);

        // Get
        var fetched = await store.GetTaskAsync(task.Id);
        fetched.Should().NotBeNull();
        fetched!.AgentId.Should().Be("agent1");
        fetched.Messages.Should().HaveCount(1);

        // Transition to Working
        (await store.UpdateTaskAsync(task.Id, TaskState.Working)).Should().BeTrue();
        (await store.GetTaskAsync(task.Id))!.State.Should().Be(TaskState.Working);

        // Complete
        (await store.CompleteTaskAsync(task.Id, "agent", "Done!")).Should().BeTrue();
        var completed = await store.GetTaskAsync(task.Id);
        completed!.State.Should().Be(TaskState.Completed);
        completed.Messages.Should().HaveCount(2);
        completed.Messages[1].Content.Should().Be("Done!");
    }

    [Fact(Skip = "Requires a live Postgres instance. Set INTEGRATION_POSTGRES_CS env var to enable.")]
    public async Task CancelTask_AfterWorking()
    {
        var cs = GetConnectionString();
        var store = new PostgresAgentTaskStore(cs, NullLogger<PostgresAgentTaskStore>.Instance);
        await store.MigrateAsync();

        var task = await store.CreateTaskAsync("s", "a", "user", "Hi");
        await store.UpdateTaskAsync(task.Id, TaskState.Working);
        (await store.CancelTaskAsync(task.Id)).Should().BeTrue();
        (await store.GetTaskAsync(task.Id))!.State.Should().Be(TaskState.Canceled);
    }

    [Fact(Skip = "Requires a live Postgres instance. Set INTEGRATION_POSTGRES_CS env var to enable.")]
    public async Task CancelTask_WhenCompleted_ReturnsFalse()
    {
        var cs = GetConnectionString();
        var store = new PostgresAgentTaskStore(cs, NullLogger<PostgresAgentTaskStore>.Instance);
        await store.MigrateAsync();

        var task = await store.CreateTaskAsync("s", "a", "user", "Hi");
        await store.CompleteTaskAsync(task.Id, "agent", "Done");
        (await store.CancelTaskAsync(task.Id)).Should().BeFalse();
    }
}
