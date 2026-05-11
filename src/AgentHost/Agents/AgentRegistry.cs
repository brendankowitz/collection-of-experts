namespace AgentHost.Agents;

/// <summary>
/// Holds references to all <see cref="IExpertAgent"/> instances in the system
/// and provides simple keyword-based routing from a free-text message to the
/// most appropriate agent.
/// </summary>
public sealed class AgentRegistry
{
    private readonly Dictionary<string, IExpertAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AgentRegistry> _logger;

    /// <summary>
    /// Creates a new <see cref="AgentRegistry"/> and populates it from DI.
    /// </summary>
    public AgentRegistry(IEnumerable<IExpertAgent> agents, ILogger<AgentRegistry> logger)
    {
        _logger = logger;
        foreach (var agent in agents)
        {
            _agents[agent.AgentId] = agent;
            _logger.LogInformation("Registered agent {AgentId}: {AgentName}", agent.AgentId, agent.Name);
        }
    }

    /// <summary>
    /// Retrieves an agent by its unique identifier.
    /// </summary>
    /// <param name="id">Agent ID (e.g., <c>fhir-server-expert</c>).</param>
    /// <returns>The agent, or <c>null</c> if not found.</returns>
    public IExpertAgent? GetAgent(string id)
        => _agents.GetValueOrDefault(id);

    /// <summary>
    /// Returns every registered agent.
    /// </summary>
    public IEnumerable<IExpertAgent> GetAllAgents()
        => _agents.Values;

    /// <summary>
    /// Routes a free-text message to the most appropriate agent using
    /// simple keyword matching. Falls back to the FHIR server agent
    /// when no keywords match.
    /// </summary>
    /// <param name="message">The user's message.</param>
    /// <returns>The selected agent.</returns>
    public IExpertAgent RouteToAgent(string message)
    {
        var lowered = message.ToLowerInvariant();

        // Healthcare shared-components keywords
        var hcKeywords = new[]
        {
            "sql connection", "retry", "blob", "storage", "exception handling",
            "mediator", "health check", "ichangefeed", "change feed",
            "configuration", "shared components", "shared-components",
            "retry wrapper", "blobclient", "transaction scope",
            "pii", "schema version", "service bus"
        };

        // FHIR server keywords
        var fhirKeywords = new[]
        {
            "fhir", "patient", "observation", "encounter", "resource",
            "bundle", "search parameter", "$export", "smart on fhir",
            "capability statement", "compartment", "valueset", "codesystem",
            "subscription", "r4", "r5", "hl7", "ndjson", "data store",
            "cosmos db fhir", "sql fhir", "fhir server"
        };

        int hcScore = hcKeywords.Count(k => lowered.Contains(k));
        int fhirScore = fhirKeywords.Count(k => lowered.Contains(k));

        _logger.LogDebug("Routing scores – HC: {HcScore}, FHIR: {FhirScore} for message: {Message}",
            hcScore, fhirScore, message);

        if (hcScore > fhirScore)
        {
            var hcAgent = _agents.Values.FirstOrDefault(a => a.AgentId == "healthcare-components-expert");
            if (hcAgent is not null)
            {
                _logger.LogInformation("Routed message to {AgentId} (score HC:{HcScore} vs FHIR:{FhirScore})",
                    hcAgent.AgentId, hcScore, fhirScore);
                return hcAgent;
            }
        }

        // Default to FHIR server agent if available
        var fhirAgent = _agents.Values.FirstOrDefault(a => a.AgentId == "fhir-server-expert")
                     ?? _agents.Values.First();

        _logger.LogInformation("Routed message to {AgentId} (score HC:{HcScore} vs FHIR:{FhirScore})",
            fhirAgent.AgentId, hcScore, fhirScore);

        return fhirAgent;
    }
}
