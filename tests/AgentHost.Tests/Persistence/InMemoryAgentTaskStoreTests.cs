using AgentHost.Repositories.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHost.Tests.Persistence;

public sealed class InMemoryAgentTaskStoreTests
{
    private readonly InMemoryAgentTaskStore _store = new(NullLogger<InMemoryAgentTaskStore>.Instance);

    [Fact]
    public async Task CreateTask_ReturnsTaskWithSubmittedState()
    {
        var task = await _store.CreateTaskAsync("session1", "agent1", "user", "Hello");

        task.Should().NotBeNull();
        task.Id.Should().NotBeNullOrEmpty();
        task.AgentId.Should().Be("agent1");
        task.SessionId.Should().Be("session1");
        task.State.Should().Be(TaskState.Submitted);
        task.Messages.Should().HaveCount(1);
        task.Messages[0].Role.Should().Be("user");
        task.Messages[0].Content.Should().Be("Hello");
    }

    [Fact]
    public async Task GetTask_ReturnsNullForUnknownId()
    {
        var result = await _store.GetTaskAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTask_ReturnsPreviouslyCreatedTask()
    {
        var created = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        var fetched = await _store.GetTaskAsync(created.Id);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task UpdateTask_ChangesState()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        var ok = await _store.UpdateTaskAsync(task.Id, TaskState.Working);

        ok.Should().BeTrue();
        var fetched = await _store.GetTaskAsync(task.Id);
        fetched!.State.Should().Be(TaskState.Working);
    }

    [Fact]
    public async Task UpdateTask_ReturnsFalseForUnknownId()
    {
        var ok = await _store.UpdateTaskAsync("bad-id", TaskState.Completed);
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task AppendMessage_AddsMessageToTask()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        var ok = await _store.AppendMessageAsync(task.Id, "agent", "Hello back!");

        ok.Should().BeTrue();
        var fetched = await _store.GetTaskAsync(task.Id);
        fetched!.Messages.Should().HaveCount(2);
        fetched.Messages[1].Role.Should().Be("agent");
        fetched.Messages[1].Content.Should().Be("Hello back!");
    }

    [Fact]
    public async Task CompleteTask_SetsCompletedStateAndAppendsMessage()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        var ok = await _store.CompleteTaskAsync(task.Id, "agent", "Done!");

        ok.Should().BeTrue();
        var fetched = await _store.GetTaskAsync(task.Id);
        fetched!.State.Should().Be(TaskState.Completed);
        fetched.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task CancelTask_SetsStateToCanceled()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        var ok = await _store.CancelTaskAsync(task.Id);

        ok.Should().BeTrue();
        var fetched = await _store.GetTaskAsync(task.Id);
        fetched!.State.Should().Be(TaskState.Canceled);
    }

    [Fact]
    public async Task CancelTask_ReturnsFalseWhenAlreadyCompleted()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        await _store.CompleteTaskAsync(task.Id, "agent", "Done");
        var ok = await _store.CancelTaskAsync(task.Id);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task StateTransitions_SubmittedToWorkingToCompleted()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "question");

        task.State.Should().Be(TaskState.Submitted);

        await _store.UpdateTaskAsync(task.Id, TaskState.Working);
        (await _store.GetTaskAsync(task.Id))!.State.Should().Be(TaskState.Working);

        await _store.CompleteTaskAsync(task.Id, "agent", "answer");
        (await _store.GetTaskAsync(task.Id))!.State.Should().Be(TaskState.Completed);
    }

    [Fact]
    public async Task UpdateTask_WithErrorMessage_StoresError()
    {
        var task = await _store.CreateTaskAsync("s", "a", "user", "Hi");
        await _store.UpdateTaskAsync(task.Id, TaskState.Failed, "something went wrong");

        var fetched = await _store.GetTaskAsync(task.Id);
        fetched!.State.Should().Be(TaskState.Failed);
        fetched.ErrorMessage.Should().Be("something went wrong");
    }
}
