using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentHost.A2A;
using AgentHost.Llm;
using AgentHost.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentHost.Agents;

/// <summary>
/// Coordinator agent that decomposes a user query into per-agent sub-queries using LLM structured output,
/// dispatches them in parallel via <see cref="IA2AClient"/>, and optionally synthesises the results.
/// Registered with ID <c>coordinator</c> and always available.
/// </summary>
public sealed class CoordinatorAgent : IExpertAgent
{
    public string AgentId => "coordinator";
    public string Name => "Coordinator";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private readonly IChatClientFactory _chatClientFactory;
    private readonly Lazy<AgentRegistry> _registryLazy;
    private readonly IA2AClient _a2aClient;
    private readonly OrchestrationOptions _options;
    private readonly ILogger<CoordinatorAgent> _logger;

    private AgentRegistry Registry => _registryLazy.Value;

    public CoordinatorAgent(
        IChatClientFactory chatClientFactory,
        Lazy<AgentRegistry> registry,
        IA2AClient a2aClient,
        IOptions<OrchestrationOptions> options,
        ILogger<CoordinatorAgent> logger)
    {
        _chatClientFactory = chatClientFactory;
        _registryLazy = registry;
        _a2aClient = a2aClient;
        _options = options.Value;
        _logger = logger;
    }

    public AgentCard GetAgentCard() => new()
    {
        AgentId = AgentId,
        Name = Name,
        Description =
            "Coordinator agent that routes queries to the most relevant expert agents, " +
            "runs them in parallel, and synthesises a unified answer.",
        Version = "1.0.0",
        Url = "http://localhost:5000",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "decompose-and-route",
                Name = "Decompose and Route",
                Description = "Decomposes a user query into sub-queries and routes each to the best expert agent.",
                ExampleQueries =
                [
                    "How does FHIR search work together with the shared SQL components?",
                    "Compare the retry patterns used in the FHIR server and the shared components library."
                ]
            },
            new AgentSkill
            {
                Id = "multi-repo-question",
                Name = "Multi-Repository Question",
                Description = "Answers questions that span multiple repositories by querying each expert in parallel.",
                ExampleQueries =
                [
                    "What are the main architectural differences between the two repos?",
                    "List all public NuGet packages across both repositories."
                ]
            }
        ]
    };

    public async Task<string> ProcessMessageAsync(string message, string sessionId)
    {
        var assignments = await DecomposeAsync(message, enableSynthesis: false);
        var results = await RunSubAgentsAsync(assignments, sessionId, CancellationToken.None);
        return FormatResults(results);
    }

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(
        string message, string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        DecompositionItem[] assignments;
        try
        {
            assignments = await DecomposeAsync(message, enableSynthesis: _options.Coordinator.EnableSynthesis);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Coordinator] Decomposition failed; falling back to direct routing.");
            assignments = BuildFallbackAssignments(message);
        }

        yield return $"[Coordinator] Routing to: {string.Join(", ", assignments.Select(a => a.AgentId))}\n\n";

        var results = await RunSubAgentsAsync(assignments, sessionId, ct);

        foreach (var (agentId, response) in results)
        {
            yield return $"## Response from `{agentId}`\n\n{response}\n\n";
        }

        if (_options.Coordinator.EnableSynthesis && results.Count >= 2)
        {
            yield return "## Synthesised Answer\n\n";
            await foreach (var chunk in SynthesizeAsync(message, results, ct))
                yield return chunk;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<DecompositionItem[]> DecomposeAsync(string message, bool enableSynthesis)
    {
        var availableAgents = Registry.GetAllAgents()
            .Where(a => !string.Equals(a.AgentId, AgentId, StringComparison.OrdinalIgnoreCase))
            .Take(_options.Coordinator.MaxParallelAgents)
            .ToList();

        if (availableAgents.Count == 0)
            return [];

        if (availableAgents.Count == 1)
        {
            return
            [
                new DecompositionItem
                {
                    AgentId = availableAgents[0].AgentId,
                    SubQuery = message,
                    Reason = "Only one agent available."
                }
            ];
        }

        var agentDescriptions = availableAgents
            .Select(a =>
            {
                var card = a.GetAgentCard();
                return $"- id: \"{a.AgentId}\" | name: \"{card.Name}\" | skills: {string.Join(", ", card.Skills.Select(s => s.Id))}";
            });

        var systemPrompt = """
            You are a routing coordinator for a multi-agent expert system.
            Given a user query and a list of available expert agents, decompose the query into
            one or more targeted sub-queries, each assigned to the most suitable agent.
            Respond with ONLY a valid JSON array (no markdown, no explanation) in this exact schema:
            [{"agent_id":"<id>","sub_query":"<targeted question>","reason":"<one-sentence rationale>"}]
            Rules:
            - Use only agent IDs from the provided list.
            - Each agent may appear at most once.
            - If the query is fully handled by one agent, return a single-element array.
            - Do not add any text outside the JSON array.
            """;

        var userPrompt = $"""
            Available agents:
            {string.Join("\n", agentDescriptions)}

            User query: {message}
            """;

        using var client = _chatClientFactory.CreateDefault();
        var response = await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        ]).ConfigureAwait(false);

        var json = response.Text?.Trim() ?? "[]";

        // Strip markdown code fences if present
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            json = start >= 0 && end > start ? json[start..(end + 1)] : "[]";
        }

        _logger.LogDebug("[Coordinator] Decomposition JSON: {Json}", json);

        try
        {
            var items = JsonSerializer.Deserialize<DecompositionItem[]>(json, JsonOptions) ?? [];
            // Filter to valid agent IDs
            var validIds = new HashSet<string>(availableAgents.Select(a => a.AgentId), StringComparer.OrdinalIgnoreCase);
            return items.Where(i => validIds.Contains(i.AgentId)).ToArray();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Coordinator] Could not parse decomposition JSON: {Json}", json);
            return BuildFallbackAssignments(message);
        }
    }

    private async Task<List<(string AgentId, string Response)>> RunSubAgentsAsync(
        DecompositionItem[] assignments, string sessionId, CancellationToken ct)
    {
        var tasks = assignments
            .Take(_options.Coordinator.MaxParallelAgents)
            .Select(async assignment =>
            {
                var uri = new Uri($"inproc://{assignment.AgentId}");
                var req = new A2ATaskSendRequest
                {
                    SessionId = sessionId,
                    Message = new Message
                    {
                        Role = "user",
                        Parts = [new TextPart { Text = assignment.SubQuery }]
                    }
                };

                var sb = new System.Text.StringBuilder();
                try
                {
                    await foreach (var update in _a2aClient.SendTaskSubscribeAsync(uri, req, ct).ConfigureAwait(false))
                    {
                        if (update.Event == "text" && update.Text != null)
                            sb.Append(update.Text);
                    }
                }
                catch (A2ADepthExceededException ex)
                {
                    _logger.LogWarning("[Coordinator] Depth exceeded calling {AgentId}: {Message}", assignment.AgentId, ex.Message);
                    sb.Append($"[Error: {ex.ErrorCode}]");
                }
                catch (A2ACycleDetectedException ex)
                {
                    _logger.LogWarning("[Coordinator] Cycle detected calling {AgentId}: {Message}", assignment.AgentId, ex.Message);
                    sb.Append($"[Error: {ex.ErrorCode}]");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Coordinator] Sub-agent {AgentId} failed.", assignment.AgentId);
                    sb.Append("[Error: sub-agent call failed]");
                }

                return (assignment.AgentId, sb.ToString());
            });

        return [.. await Task.WhenAll(tasks)];
    }

    private async IAsyncEnumerable<string> SynthesizeAsync(
        string originalQuery,
        List<(string AgentId, string Response)> results,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var context = string.Join("\n\n", results.Select(r => $"### {r.AgentId}\n{r.Response}"));
        var prompt = $"""
            Original question: {originalQuery}

            Answers from individual expert agents:
            {context}

            Synthesise all the above answers into a single, coherent, non-repetitive response.
            """;

        using var client = _chatClientFactory.CreateDefault();
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }

    private static string FormatResults(List<(string AgentId, string Response)> results)
    {
        if (results.Count == 0) return "[Coordinator] No agents available to handle this query.";
        if (results.Count == 1) return results[0].Response;

        return string.Join("\n\n", results.Select(r => $"## {r.AgentId}\n{r.Response}"));
    }

    private DecompositionItem[] BuildFallbackAssignments(string message)
    {
        // Fall back to sending the full message to every available agent (up to max)
        return Registry.GetAllAgents()
            .Where(a => !string.Equals(a.AgentId, AgentId, StringComparison.OrdinalIgnoreCase))
            .Take(_options.Coordinator.MaxParallelAgents)
            .Select(a => new DecompositionItem { AgentId = a.AgentId, SubQuery = message, Reason = "fallback" })
            .ToArray();
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class DecompositionItem
    {
        [JsonPropertyName("agent_id")]
        public string AgentId { get; set; } = "";

        [JsonPropertyName("sub_query")]
        public string SubQuery { get; set; } = "";

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";
    }
}
