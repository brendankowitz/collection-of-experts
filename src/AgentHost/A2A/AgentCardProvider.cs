using System.Collections.Concurrent;

namespace AgentHost.A2A;

public sealed class AgentCardProvider
{
    private readonly ConcurrentDictionary<string, AgentCard> _cards = new(StringComparer.OrdinalIgnoreCase);

    public AgentCardProvider()
    {
        RegisterFhirServerCard();
        RegisterHealthcareComponentsCard();
    }

    public AgentCard? GetCard(string agentId)
        => _cards.GetValueOrDefault(agentId);

    public IEnumerable<AgentCard> GetAllCards()
        => _cards.Values;

    public void RegisterCard(AgentCard card)
        => _cards[card.AgentId] = card;

    public bool UnregisterCard(string agentId)
        => _cards.TryRemove(agentId, out _);

    private void RegisterFhirServerCard()
    {
        RegisterCard(new AgentCard
        {
            AgentId = "fhir-server-expert",
            Name = "FHIR Server Expert",
            Description =
                "Expert agent specialised in the Microsoft FHIR Server for Azure. " +
                "Can answer architecture questions, search the codebase, and guide pull-request creation.",
            Version = "1.0.0",
            Url = "http://localhost:5001",
            Capabilities = new AgentCapabilities { Streaming = true },
            Skills =
            [
                new AgentSkill
                {
                    Id = "code-search",
                    Name = "FHIR Code Search",
                    Description = "Search the microsoft/fhir-server repository for classes, methods, and configuration files.",
                    ExampleQueries =
                    [
                        "Find the search parameter registry implementation",
                        "Show me how $export is implemented",
                        "Where is the SQL search provider?"
                    ]
                },
                new AgentSkill
                {
                    Id = "architecture-qa",
                    Name = "FHIR Architecture Q&A",
                    Description = "Explain architectural decisions, data flow, and component interactions in the FHIR server.",
                    ExampleQueries =
                    [
                        "How does the server handle R4 search parameters?",
                        "Explain the data layer abstraction",
                        "What is the request pipeline?"
                    ]
                },
                new AgentSkill
                {
                    Id = "pr-guidance",
                    Name = "PR Guidance",
                    Description = "Provide step-by-step guidance for creating pull requests against the FHIR server repository.",
                    ExampleQueries =
                    [
                        "How do I submit a PR for a new search parameter?",
                        "What tests are required for a data layer change?",
                        "Branch naming conventions"
                    ]
                }
            ]
        });
    }

    private void RegisterHealthcareComponentsCard()
    {
        RegisterCard(new AgentCard
        {
            AgentId = "healthcare-components-expert",
            Name = "Healthcare Shared Components Expert",
            Description =
                "Expert agent specialised in the Microsoft Healthcare Shared Components library. " +
                "Covers SQL connection management, blob storage, exception handling, configuration, and health checks.",
            Version = "1.0.0",
            Url = "http://localhost:5002",
            Capabilities = new AgentCapabilities { Streaming = true },
            Skills =
            [
                new AgentSkill
                {
                    Id = "code-search",
                    Name = "Shared Components Code Search",
                    Description = "Search the microsoft/healthcare-shared-components repository for utilities, wrappers, and base classes.",
                    ExampleQueries =
                    [
                        "Find RetrySqlConnectionWrapper",
                        "Show me the blob client implementation",
                        "Where are health checks defined?"
                    ]
                },
                new AgentSkill
                {
                    Id = "architecture-qa",
                    Name = "Shared Components Architecture Q&A",
                    Description = "Explain how the shared components fit together and how they are consumed by downstream services.",
                    ExampleQueries =
                    [
                        "How does the retry wrapper work?",
                        "Explain the Mediator pattern usage",
                        "What is IChangeFeedSource?"
                    ]
                },
                new AgentSkill
                {
                    Id = "pr-guidance",
                    Name = "PR Guidance",
                    Description = "Provide step-by-step guidance for creating pull requests against the shared-components repository.",
                    ExampleQueries =
                    [
                        "How do I version a new shared package?",
                        "What unit-test coverage is required?",
                        "Dependency upgrade checklist"
                    ]
                }
            ]
        });
    }
}
