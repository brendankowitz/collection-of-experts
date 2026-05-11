using System.Net;
using System.Text;
using System.Text.Json;
using AgentHost.A2A;
using AgentHost.Agents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using AgentHost.Orchestration;
using A2ATaskStatus = AgentHost.A2A.TaskStatus;

namespace AgentHost.Tests;

/// <summary>Tests for HttpA2AClient (tests 1 + 2) and InProcessA2AClient (test 3).</summary>
public sealed class A2AClientTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ── Test 1: HttpA2AClient.SendTaskAsync round-trips JSON ─────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HttpA2AClient_SendTaskAsync_RoundTripsJson()
    {
        // Arrange
        var expectedTask = new AgentTask
        {
            Id = "abc123",
            AgentId = "fhir-server-expert",
            SessionId = "sess1",
            Status = A2ATaskStatus.Completed,
            Messages =
            [
                new Message { Role = "agent", Parts = [new TextPart { Text = "Hello from FHIR" }] }
            ]
        };
        var responseBody = JsonSerializer.Serialize(new TaskResponse { Task = expectedTask });

        var fakeHandler = new FakeHttpMessageHandler(responseBody, "application/json");
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("http://localhost:5000") };
        var factory = new FakeHttpClientFactory(httpClient);
        var client = new HttpA2AClient(factory, Options.Create(new OrchestrationOptions()), NullLogger<HttpA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            SessionId = "sess1",
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "test question" }] }
        };

        // Act
        var result = await client.SendTaskAsync(new Uri("http://localhost:5000"), req);

        // Assert
        result.Id.Should().Be("abc123");
        result.Status.Should().Be(A2ATaskStatus.Completed);
        result.Messages.Should().ContainSingle(m => m.Role == "agent");
        var text = result.Messages.First(m => m.Role == "agent").Parts.OfType<TextPart>().First().Text;
        text.Should().Be("Hello from FHIR");
    }

    // ── Test 2: HttpA2AClient.SendTaskSubscribeAsync parses 3-chunk SSE stream ─

    [Fact]
    [Trait("Category", "Unit")]
    public async Task HttpA2AClient_SendTaskSubscribeAsync_Parses3ChunkSseStream()
    {
        // Arrange – build an SSE body with 3 text events
        var chunks = new[] { "Hello", " world", "!" };
        var ssebody = new StringBuilder();
        ssebody.AppendLine($"data: {JsonSerializer.Serialize(new TaskEvent { Event = "status", Status = A2ATaskStatus.Working })}");
        ssebody.AppendLine();
        foreach (var chunk in chunks)
        {
            ssebody.AppendLine($"data: {JsonSerializer.Serialize(new TaskEvent { Event = "text", Text = chunk })}");
            ssebody.AppendLine();
        }
        ssebody.AppendLine($"data: {JsonSerializer.Serialize(new TaskEvent { Event = "done" })}");
        ssebody.AppendLine();

        var fakeHandler = new FakeHttpMessageHandler(ssebody.ToString(), "text/event-stream");
        var httpClient = new HttpClient(fakeHandler);
        var factory = new FakeHttpClientFactory(httpClient);
        var client = new HttpA2AClient(factory, Options.Create(new OrchestrationOptions()), NullLogger<HttpA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "stream me" }] }
        };

        // Act
        var received = new List<A2ATaskUpdate>();
        await foreach (var update in client.SendTaskSubscribeAsync(new Uri("http://fake-agent:9999"), req))
            received.Add(update);

        // Assert: 3 text chunks + status events
        var textChunks = received.Where(u => u.Event == "text").ToList();
        textChunks.Should().HaveCount(3);
        textChunks[0].Text.Should().Be("Hello");
        textChunks[1].Text.Should().Be(" world");
        textChunks[2].Text.Should().Be("!");
    }

    // ── Test 3: InProcessA2AClient calls agent directly ──────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InProcessA2AClient_SendTaskAsync_CallsAgentDirectly()
    {
        // Arrange
        var fakeAgent = new FakeExpertAgent("test-agent", "Test Agent", "in-process response");
        var logger = NullLogger<AgentRegistry>.Instance;
        var registry = new AgentRegistry([fakeAgent], logger);
        var client = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), Options.Create(new OrchestrationOptions()), NullLogger<InProcessA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            SessionId = "s1",
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "hello" }] }
        };

        // Act
        var result = await client.SendTaskAsync(new Uri("inproc://test-agent"), req);

        // Assert
        result.AgentId.Should().Be("test-agent");
        result.Status.Should().Be(A2ATaskStatus.Completed);
        var agentMsg = result.Messages.First(m => m.Role == "agent");
        agentMsg.Parts.OfType<TextPart>().First().Text.Should().Be("in-process response");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InProcessA2AClient_SendTaskSubscribeAsync_StreamsChunks()
    {
        // Arrange
        var fakeAgent = new FakeExpertAgent("stream-agent", "Stream Agent", "hello world");
        var registry = new AgentRegistry([fakeAgent], NullLogger<AgentRegistry>.Instance);
        var client = new InProcessA2AClient(new Lazy<AgentRegistry>(() => registry), Options.Create(new OrchestrationOptions()), NullLogger<InProcessA2AClient>.Instance);

        var req = new A2ATaskSendRequest
        {
            Message = new Message { Role = "user", Parts = [new TextPart { Text = "stream" }] }
        };

        // Act
        var updates = new List<A2ATaskUpdate>();
        await foreach (var u in client.SendTaskSubscribeAsync(new Uri("inproc://stream-agent"), req))
            updates.Add(u);

        // Assert
        updates.Should().Contain(u => u.Event == "done");
        var textUpdates = updates.Where(u => u.Event == "text").ToList();
        textUpdates.Should().NotBeEmpty();
        var combined = string.Concat(textUpdates.Select(u => u.Text));
        combined.Should().Contain("hello");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeHttpMessageHandler(string responseBody, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, contentType)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
