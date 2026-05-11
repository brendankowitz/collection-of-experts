using AgentHost.A2A;

namespace AgentHost.Agents;

public sealed class AgentRegistry
{
    private readonly Dictionary<string, IExpertAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<AgentRegistry> _logger;

    public AgentRegistry(IEnumerable<IExpertAgent> agents, ILogger<AgentRegistry> logger)
    {
        _logger = logger;
        foreach (var agent in agents)
        {
            _agents[agent.AgentId] = agent;
            _logger.LogInformation("Registered agent {AgentId}: {AgentName}", agent.AgentId, agent.Name);
        }
    }

    public IExpertAgent? GetAgent(string id)
        => _agents.GetValueOrDefault(id);

    public IEnumerable<IExpertAgent> GetAllAgents()
        => _agents.Values;

    public IEnumerable<AgentCard> GetAgentCards()
        => _agents.Values.Select(a => a.GetAgentCard());

    public void RegisterAgent(IExpertAgent agent)
    {
        _agents[agent.AgentId] = agent;
        _logger.LogInformation("Registered agent {AgentId}: {AgentName}", agent.AgentId, agent.Name);
    }

    public bool UnregisterAgent(string agentId)
    {
        if (_agents.Remove(agentId))
        {
            _logger.LogInformation("Unregistered agent {AgentId}", agentId);
            return true;
        }
        return false;
    }

    public IExpertAgent RouteToAgent(string message)
    {
        var lowered = message.ToLowerInvariant();

        var hcKeywords = new[]
        {
            "sql connection", "retry", "blob", "storage", "exception handling",
            "mediator", "health check", "ichangefeed", "change feed",
            "configuration", "shared components", "shared-components",
            "retry wrapper", "blobclient", "transaction scope",
            "pii", "schema version", "service bus"
        };

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

        _logger.LogDebug("Routing scores - HC: {HcScore}, FHIR: {FhirScore} for message: {Message}",
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

        var fhirAgent = _agents.Values.FirstOrDefault(a => a.AgentId == "fhir-server-expert")
                     ?? _agents.Values.First();

        _logger.LogInformation("Routed message to {AgentId} (score HC:{HcScore} vs FHIR:{FhirScore})",
            fhirAgent.AgentId, hcScore, fhirScore);

        return fhirAgent;
    }
}
