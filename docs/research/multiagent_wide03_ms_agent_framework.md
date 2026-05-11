## Facet: Microsoft Agent Framework & .NET Agent Ecosystem

### Key Findings

- **Microsoft Agent Framework (MAF) v1.0 GA released April 2026**, converging Semantic Kernel and AutoGen into a unified runtime for .NET and Python [^137^]. It represents the strategic consolidation of Microsoft's two agent frameworks into a single foundation.

- **Multi-agent orchestration patterns** include sequential, concurrent, handoff, group chat, and Magentic-One — all supporting streaming, checkpointing, human-in-the-loop approvals, and pause/resume [^137^][^136^].

- **A2A and MCP are natively supported**: MCP enables dynamic tool discovery from external servers; A2A enables cross-runtime agent collaboration (A2A 1.0 coming soon) [^137^][^26^].

- **Azure AI Foundry provides managed hosting** via Foundry Agent Service, with containerized deployments, automatic RBAC, and playground integration [^87^][^88^].

- **Declarative agents and workflows** can be defined in YAML/JSON for version-controlled, templatized agent configuration [^137^][^190^].

- **Pluggable memory architecture** supports Mem0, Redis, Neo4j, Pinecone, Qdrant, Weaviate, Elasticsearch, Postgres, and Foundry Agent Service [^137^][^227^][^221^].

- **OpenTelemetry is built-in** with native Azure Monitor integration, distributed tracing, metrics, and cost transparency via token consumption tracking [^139^][^223^].

- **Human-in-the-loop** uses `FunctionApprovalRequestContent`/`FunctionApprovalResponseContent` with tool-level approval modes: `always_require`, `never_require`, `conditional` [^134^][^143^][^149^].

- **Migration guides exist** for both Semantic Kernel and AutoGen, with code-side-by-side comparisons and migration assistants [^186^][^184^].

- **Interview Coach sample app** demonstrates production patterns: MAF + Foundry + MCP + Aspire + handoff orchestration [^219^].

---

### Major Players & Sources

| Entity | Role/Relevance |
|--------|---------------|
| **Microsoft Agent Framework (MAF)** | Unified open-source SDK for .NET and Python; successor to Semantic Kernel + AutoGen [^197^] |
| **Semantic Kernel** | Enterprise-grade planning foundation absorbed into MAF [^91^] |
| **AutoGen** | Multi-agent orchestration patterns (GroupChat, Magentic-One) ported to MAF [^184^] |
| **Azure AI Foundry** | Managed hosting platform for agent deployment, observability, and tool ecosystem [^87^] |
| **Microsoft.Extensions.AI (MEAI)** | Foundation abstraction layer; `IChatClient` universal interface for any model [^92^] |
| **Neo4j** | Graph-native memory provider (short-term, long-term, reasoning memory) [^221^][^222^] |
| **Mem0** | Intelligent memory extraction with Redis backend [^227^] |
| **KPMG** | Early adopter — built regulatory/tax document analysis agent in Foundry [^198^] |
| **Commerzbank** | Early production adopter for customer support automation [^13^] |
| **Fujitsu** | Early production adopter for human-AI workflow integration [^13^] |
| **AG-UI Protocol** | Open standard for agent-user interface communication [^216^][^217^] |
| **CopilotKit** | Frontend UI components supporting AG-UI protocol [^221^] |
| **MCP (Model Context Protocol)** | Open standard for tool discovery and invocation [^12^] |
| **A2A Protocol** | Agent-to-Agent protocol for cross-framework collaboration [^137^] |

---

### Trends & Signals

- **Framework consolidation**: The multi-agent framework space is consolidating from "many experiments" to "a few serious platforms" from major tech companies [^89^]. Microsoft's unification of Semantic Kernel and AutoGen signals this trend.

- **Protocol-driven interoperability**: MCP for tools, A2A for agent-to-agent communication, and AG-UI for agent-user interfaces form a three-layer protocol stack for agentic systems [^228^].

- **"Configuration as Code"**: Declarative YAML agents enable version-controlled agent definitions that can be designed in Foundry's low-code UI and promoted directly to production runtime [^196^][^190^].

- **Graph-based memory over flat RAG**: Context providers like Neo4j Agent Memory use knowledge graphs for persistent, relationship-aware memory that compounds over sessions [^221^][^222^].

- **Local + cloud hybrid**: Foundry Local enables on-device SLM development that integrates seamlessly with cloud deployment via the same framework [^218^].

- **DevUI for agent debugging**: Browser-based visual debugger for real-time agent execution tracing — described as "F12 Developer Tools for AI agents" [^222^][^223^].

- **Migration wave**: Active migration from both Semantic Kernel and AutoGen to MAF, with detailed guides and migration assistants analyzing existing code [^186^][^184^].

---

### Controversies & Conflicting Claims

- **Magentic orchestration not yet in C#**: The Magentic-One pattern (most powerful orchestrator) is Python-only as of early 2026; C# support is pending [^146^][^184^].

- **Human-in-the-loop limitations in hosted agents**: GitHub issue #5654 confirms that `function_approval_request` content type is not yet supported in hosted Agent Framework scenarios, breaking tool approval workflows in hosted deployments [^135^].

- **Durability concerns**: Critics note that Agent Framework's checkpoint-based approach may not guarantee full durability for long-running workflows compared to per-message persistence systems like Strands Agents [^193^].

- **AutoGen cost reality**: One 8-agent GPT-4o conversation can cost $5–30 per run; token explosion and context window exhaustion are real production concerns [^91^].

- **Higher abstraction vs. control tradeoff**: Like CrewAI, MAF's orchestration patterns provide less fine-grained control than LangGraph's explicit graph definition, which may limit certain complex use cases [^182^].

- **Documentation API churn**: Multiple sources note that API changes frequently during preview/RC phase, though v1.0 GA promises backward compatibility [^92^][^182^].

---

### Recommended Deep-Dive Areas

| Area | Why It Warrants Depth |
|------|----------------------|
| **Declarative workflow YAML → production pipeline** | The "design in UI, deploy as YAML" pattern is a genuine differentiator for enterprise SDLC integration |
| **Handoff orchestration at scale** | Microsoft's most novel pattern; the directed-graph-with-agent-routing model has implications for conversational AI architecture |
| **Neo4j/Mem0 memory providers** | Graph-native memory with entity extraction and reasoning traces represents a step beyond simple RAG |
| **Foundry Agent Service hosting economics** | Container-per-agent with dedicated Entra identity has cost and security implications for large deployments |
| **MCP + A2A dual protocol support** | How agents simultaneously use MCP for tools and A2A for cross-agent communication needs architectural documentation |
| **AG-UI + CopilotKit frontend integration** | Standardized agent UI protocol with multiple frontend options enables rapid user-facing agent deployment |
| **Checkpointing and state hydration** | Critical for production long-running workflows; superstep-based checkpoint model needs operational understanding |

---

### Detailed Notes

#### 1. Framework Overview and Convergence

Microsoft Agent Framework was announced at .NET Conf 2025 and reached GA in April 2026 [^137^][^186^]. It unifies two previously separate Microsoft agent frameworks:

- **Semantic Kernel**: Enterprise-grade foundations (planning, plugins, memory, DI integration)
- **AutoGen**: Multi-agent orchestration patterns (GroupChat, Magentic-One, conversational agents)

The convergence addresses what Microsoft saw as ecosystem fragmentation. From the official blog [^199^]:

> "Microsoft Agent Framework is the convergence of Semantic Kernel and AutoGen into a single, unified runtime that combines Semantic Kernel's enterprise foundations with AutoGen's multi-agent orchestration patterns."

Key architectural principles [^197^]:
- **Python and C#/.NET** with consistent APIs across both languages
- **100% open source** on GitHub (microsoft/agent-framework)
- **Built on Microsoft.Extensions.AI** — the standardized `IChatClient` abstraction
- **Graph-based workflow engine** for deterministic, repeatable multi-agent processes
- **Middleware pipeline** for cross-cutting concerns (safety, logging, compliance)

#### 2. .NET API Surface for Creating Agents

The core .NET API centers on `AIAgent` and `IChatClient`:

```csharp
// Basic agent creation
using Microsoft.Agents.AI;

AIAgent agent = new OpenAIClient(...)
    .GetResponsesClient("gpt-4.1")
    .AsAIAgent(
        name: "HaikuBot", 
        instructions: "You are an upbeat assistant that writes beautifully.");

// Running the agent
Console.WriteLine(await agent.RunAsync("Write a haiku about Microsoft Agent Framework."));
```

Key NuGet packages [^228^][^92^]:
- `Microsoft.Agents.AI` — Core framework (main package)
- `Microsoft.Agents.AI.OpenAI` — OpenAI/Azure OpenAI connector
- `Microsoft.Agents.AI.Workflows` — Multi-agent workflow orchestration
- `Microsoft.Agents.AI.DevUI` — Development/debugging UI
- `Microsoft.Agents.AI.Hosting` — ASP.NET hosting integration

The `AsAIAgent()` extension method on `IChatClient` is the primary entry point [^26^]. Agents support:
- `RunAsync()` — non-streaming execution
- `RunStreamingAsync()` — token-by-token streaming via `IAsyncEnumerable`
- Tools via `AIFunction` / `[AIFunction]` attributes
- MCP server tools via `McpServerConfig`
- Context providers for memory

**Foundry integration** uses `AIProjectClient` [^26^]:
```csharp
AIAgent agent = new AIProjectClient(
    new Uri("https://your-foundry-service.services.ai.azure.com/api/projects/your-project"),
    new AzureCliCredential())
    .AsAIAgent(model: "gpt-5.4-mini", instructions: "You are a friendly assistant.");
```

#### 3. Multi-Agent Workflow Patterns

Microsoft Agent Framework supports five orchestration patterns [^136^][^137^]:

**Sequential**: Agents process one after another, each output feeding the next.
```csharp
Workflow pipeline = AgentWorkflowBuilder.BuildSequential(researcher, writer, editor);
```

**Concurrent**: Fan-out to multiple agents in parallel, then fan-in results.
```csharp
// Uses AddFanOutEdge / AddFanInEdge with custom aggregation executors
var workflow = new WorkflowBuilder(startExecutor)
    .AddFanOutEdge(startExecutor, targets: [researcherAgent, plannerAgent])
    .AddFanInEdge(aggregationExecutor, sources: [researcherAgent, plannerAgent])
    .Build();
```

**Handoff**: Directed graph where agents decide routing via synthetic handoff tools. Each agent sees declared edges as callable tools [^140^]:
```csharp
Workflow workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(triage)
    .WithHandoff(triage, billing)
    .WithHandoff(triage, tech)
    .Build();
```

The handoff pattern is Microsoft's most novel contribution. The framework injects synthetic tool calls for each declared edge; routing decisions stay with the agents while topology and guardrails stay with the developer [^140^].

**Group Chat**: Multiple agents converse in a shared thread with a selection strategy determining who speaks next. Supports round-robin and custom selectors [^184^].

**Magentic-One (Python only)**: A dedicated LLM-powered manager coordinates specialized agents through dynamic task planning, progress tracking, and adaptive replanning. Includes human-in-the-loop plan review and stall intervention [^146^][^225^]:
```python
workflow = (
    MagenticBuilder()
    .participants(researcher=research_agent, writer=writing_agent)
    .with_standard_manager(chat_client=chat_client)
    .with_plan_review(enable=True)
    .with_checkpointing(checkpoint_storage)
    .build()
)
```

All patterns support: streaming, checkpointing, human-in-the-loop approvals, pause/resume for long-running workflows [^137^].

#### 4. A2A and MCP Protocol Support

**MCP (Model Context Protocol)**:
- Agents dynamically discover and invoke tools from MCP-compliant servers [^137^]
- Configuration via `McpServerConfig` in .NET:
```csharp
var agent = chatClient.AsAIAgent(
    name: "DevAgent",
    instructions: "You help developers with their codebase.",
    mcpServers: [
        new McpServerConfig("filesystem", "uvx mcp-server-filesystem /workspace"),
        new McpServerConfig("github", "uvx mcp-server-github")
    ]
);
```
- Works with any MCP server (filesystem, GitHub, databases, APIs) [^12^]

**A2A (Agent-to-Agent Protocol)**:
- Enables cross-runtime agent collaboration [^137^]
- Agents can coordinate with agents running in other frameworks (LangGraph, CrewAI, etc.)
- Uses structured, protocol-driven messaging
- A2A 1.0 support "coming soon" as of v1.0 release

The dual protocol support positions MAF as an interoperability hub:
- **MCP gives agents tools** (from external servers)
- **A2A lets agents communicate with other agents** (across frameworks)
- **AG-UI brings agents into user-facing applications** [^228^]

#### 5. Azure AI Foundry's Role in Agent Hosting

Azure AI Foundry provides three hosting tiers [^87^][^88^]:

**Foundry Agent Service (managed)**:
- Containerized agent deployment with automatic infrastructure provisioning
- Each agent gets a dedicated Entra service principal identity
- Supports both "responses" and "invocations" protocols
- CPU/memory allocation configurable (e.g., 1 CPU / 2Gi memory)
- Versioned deployments with zero-downtime updates
- Built-in RBAC (Azure AI Project Manager role required)

**Hosted Agent lifecycle** [^87^]:
1. Build and push container to Azure Container Registry
2. Create agent version (triggers infrastructure provisioning)
3. Poll for `active` status (< 1 minute typical)
4. Invoke via dedicated endpoint

**Deployment methods**:
- **Azure Developer CLI (azd)**: One-command deployment with automatic RBAC
- **VS Code Extension**: Visual deployment with playground integration
- **Python SDK / REST API**: Programmatic management

**Foundry integration for non-hosted agents** [^88^]:
- Model backend for local/runtime agents
- Managed tool ecosystem (web search, file search, code interpreter)
- Observability dashboards (traces, metrics, evaluations)
- Playground for interactive testing

KPMG case study [^198^]:
> "KPMG International built a custom AI agent within Microsoft Foundry to analyze regulatory and tax documents, surface relevant guidance, and help professionals deliver insights faster across global markets."

#### 6. Plugin System and Extensibility

The framework provides multiple extension mechanisms [^137^][^195^]:

**Middleware Pipeline** (three interception points):
- **AgentMiddleware** — turn-level: security screening, content policy, audit logging
- **FunctionMiddleware** — tool-level: execution timing, argument validation, tool budgets
- **ChatMiddleware** — model-level: raw message inspection, token enforcement, caching

```csharp
// Middleware example
async Task loggingMiddleware(AgentContext context, Func<Task> next)
{
    Console.WriteLine($"Agent {context.Agent.Name} starting");
    await next();
    Console.WriteLine($"Agent {context.Agent.Name} completed");
}
```

**Context Providers**: Pluggable hooks that inject context before each turn and persist state after:
- Neo4j (graph-native memory + knowledge retrieval)
- Redis / Azure Managed Redis (with Mem0)
- Mem0 (intelligent memory extraction)
- Pinecone, Qdrant, Weaviate, Elasticsearch, Postgres
- Foundry Agent Service (managed)
- Custom stores via interface implementation

**Declarative Configuration**:
- YAML/JSON agent definitions [^190^]:
```yaml
kind: Prompt
name: DiagnosticAgent
instructions: Specialized diagnostic and issue detection agent...
model:
  id: gpt-4o-mini
  options:
    temperature: 0.9
tools:
  - kind: web_search
    name: WebSearchTool
```

**Skills**: Reusable domain capability packages (instructions + scripts + resources) [^137^].

#### 7. Observability Features

**OpenTelemetry Integration** is built-in [^139^][^223^]:

```csharp
// OpenTelemetry setup for Azure Monitor
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("Experimental.Microsoft.Agents.AI")
    .AddAzureMonitorTraceExporter(options => 
        options.ConnectionString = appInsightsConnectionString)
    .Build();
```

Default telemetry source names:
- `Experimental.Microsoft.Agents.AI` — default if not specified
- `Microsoft.Extensions.AI` — MEAI integration
- `*Microsoft.Extensions.Agents*` — agent-specific spans

**Traced attributes** include [^90^]:
- `gen_ai.request.model` — model/deployment name
- `gen_ai.usage.input_tokens` — prompt tokens
- `gen_ai.usage.output_tokens` — completion tokens
- `gen_ai.response.finish_reasons` — stop reasons
- Full prompt/completion text (when sensitive data enabled)

**Visualization options** [^223^]:
| Platform | Use Case |
|----------|----------|
| **Aspire Dashboard** | Local development (OTLP endpoint) |
| **Application Insights** | Production monitoring |
| **Foundry Portal** | Integrated traces dashboard |
| **Grafana Dashboards** | Advanced visualization |

**DevUI** (local debugging) [^222^][^223^]:
- Chain of thought visualization (reasoning → action → observation)
- Real-time state monitoring
- Message flow between agents
- Workflow topology diagrams
- Auto-starts at `http://localhost:8090` (Python) or `/devui` (.NET)

```csharp
// Enable DevUI in ASP.NET
if (builder.Environment.IsDevelopment())
{
    app.MapDevUI();  // Serves at /devui
}
```

#### 8. Human-in-the-Loop

The framework supports two HITL mechanisms [^134^][^143^][^144^]:

**Tool Approval Workflow**:
- Uses `FunctionApprovalRequestContent` / `FunctionApprovalResponseContent`
- Three approval modes: `always_require`, `never_require`, `conditional`
- The agent proposes a tool call and waits for human approval

```csharp
// Wrap function with approval requirement
AIFunction paymentFunctionWithApproval =
    new ApprovalRequiredAIFunction(
        AIFunctionFactory.Create(PaymentsAgent.ProcessPaymentAsync));

// Approval loop pattern
while (userInputRequests.Count > 0)
{
    var userInputResponses = userInputRequests
        .OfType<FunctionApprovalRequestContent>()
        .Select(req => {
            Console.WriteLine($"Function: {req.FunctionCall.Name}");
            bool approved = GetHumanApproval();
            return new ChatMessage(ChatRole.User, 
                [req.CreateResponse(approved)]);
        }).ToList();
    
    response = await agent.RunAsync(userInputResponses, agentThread);
}
```

**Workflow RequestPort Pattern**:
- `RequestInfoEvent` emitted when workflow needs human input
- Integrates with AG-UI for web-based approval workflows [^138^][^217^]
- Supports interrupt events, approval dialogs, and resume logic

```python
# Python tool decorator with approval
@tool(approval_mode="always_require")
def send_email(to: str, subject: str, body: str) -> str:
    """Send an email to the specified recipient."""
    return f"Email sent to {to}"
```

**HITL in hosted agents**: Currently limited — GitHub issue #5654 confirms that hosted Agent Framework scenarios don't yet support `function_approval_request` content type [^135^].

**HITL in Magentic orchestration**: Supports plan review (approve/revise plans before execution), stall intervention, and tool approval via `MagenticHumanInterventionRequest` events [^225^].

#### 9. Real Code Samples for .NET Agent Creation

**Minimal agent** [^186^]:
```csharp
using Azure.Identity;
using Microsoft.Agents.AI;
using OpenAI;

var agent = new OpenAIClient(
    new BearerTokenPolicy(new AzureCliCredential(), "https://ai.azure.com/.default"),
    new OpenAIClientOptions() { Endpoint = new Uri("https://<resource>.openai.azure.com/openai/v1") })
    .GetResponsesClient("gpt-4.1")
    .AsAIAgent(name: "HaikuBot", instructions: "You are an upbeat assistant.");

Console.WriteLine(await agent.RunAsync("Write a haiku about Microsoft Agent Framework."));
```

**Agent with tools** [^228^]:
```csharp
public static class ChristmasTools
{
    [Description("Suggest a Christmas gift based on the budget in USD.")]
    public static string SuggestGift([Description("Budget in USD")] decimal budget)
    {
        if (budget < 20) return "A festive mug + hot cocoa mix";
        if (budget < 100) return "A good hardcover book and holiday candle";
        return "A premium smartwatch or a luxury gift box";
    }
}
```

**Streaming agent** [^90^]:
```csharp
public async IAsyncEnumerable<string> WriteStreamingAsync(string context)
{
    await foreach (var update in adrWriterAgent.RunStreamingAsync(context))
    {
        if (!string.IsNullOrEmpty(update.Text))
            yield return update.Text;
    }
}
```

**Multi-agent handoff (Interview Coach)** [^219^]:
```csharp
var workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(triageAgent)
    .WithHandoffs(triageAgent, [receptionistAgent, behaviouralAgent, technicalAgent, summariserAgent])
    .WithHandoffs(receptionistAgent, [behaviouralAgent, triageAgent])
    .WithHandoffs(behaviouralAgent, [technicalAgent, triageAgent])
    .Build();
```

**Declarative workflow** [^191^]:
```csharp
string workflowPath = Path.Combine(AppContext.BaseDirectory, "greeting-workflow.yaml");
Workflow workflow = DeclarativeWorkflowBuilder.Build<string>(workflowPath, options);
StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, input, checkpointManager);
```

#### 10. Comparison with LangGraph and CrewAI

| Dimension | Microsoft Agent Framework | LangGraph | CrewAI |
|-----------|--------------------------|-----------|--------|
| **Orchestration model** | Graph-based with 5 patterns (sequential, concurrent, handoff, group chat, Magentic) | Directed graph with conditional edges | Role-based crews with process types |
| **State persistence** | Checkpointing (superstep-based) with hydration | Built-in checkpointing with "time travel" | Task outputs passed sequentially |
| **Languages** | .NET + Python (consistent APIs) | Python only (TS in beta) | Python only |
| **Learning curve** | Medium (familiar for .NET/ASP.NET developers) | Medium-High (graph concepts, state schemas) | Low (role-based DSL, intuitive) |
| **Production readiness** | High (v1.0 GA, OpenTelemetry, Azure integration) | Highest (LangSmith, 70M+ monthly downloads) | Medium (growing, limited checkpointing) |
| **Streaming** | Per-token via IAsyncEnumerable / async generators | Per-node token streaming | Limited |
| **Multi-provider** | Foundry, Azure OpenAI, OpenAI, Anthropic, Bedrock, Gemini, Ollama | Any (via LangChain integrations) | Any (via LangChain) |
| **Declarative config** | YAML agents and workflows | Some (via LangGraph Studio) | Limited |
| **Memory** | Pluggable (Redis, Neo4j, Mem0, Pinecone, Qdrant, etc.) | LangChain memory ecosystem | Built-in RAG, short/long-term |
| **Human-in-the-loop** | Tool approval + AG-UI protocol | Native checkpoint interrupts | Basic delegation |
| **Unique strengths** | .NET native, handoff pattern, Foundry integration, DevUI | Graph visualization, time-travel, largest ecosystem | Fastest prototyping, role-based design |

**Key differentiators for MAF** [^182^][^181^]:
- **Only major framework with first-class .NET support** — native C# APIs, ASP.NET integration, DI
- **Handoff pattern** — unique directed-graph-with-agent-routing model
- **Azure ecosystem integration** — Foundry hosting, Entra ID, Content Safety, Monitor
- **DevUI** — purpose-built agent debugging visualization
- **MCP + A2A + AG-UI** — triple protocol support for tools, agent comms, and UI

**When to choose MAF**:
- .NET / Microsoft ecosystem shops
- Enterprise scenarios requiring Entra auth, Content Safety, Azure Monitor
- Complex multi-agent orchestration with checkpointing
- Teams wanting to migrate from existing Semantic Kernel / AutoGen code

**When to choose LangGraph**:
- Maximum control over graph topology
- Python-only shops already in LangChain ecosystem
- Need for graph visualization and time-travel debugging
- Largest community and integration ecosystem

**When to choose CrewAI**:
- Fastest prototyping and iteration
- Role-based mental model fits the problem domain
- Non-technical stakeholders need to understand architecture
- Simple sequential/hierarchical workflows

#### 11. Migration from Semantic Kernel and AutoGen

**Semantic Kernel → MAF** [^12^]:
| SK Component | MAF Equivalent |
|-------------|----------------|
| `Kernel` | `IAIAgent` implementations |
| `KernelFunction` | `AIFunction` |
| `IChatCompletionService` | `IChatClient` + agent `RunAsync()` |
| `AgentGroupChat` | `AgentWorkflowBuilder` workflows |
| Planners (Handlebars, Stepwise) | Sequential workflows + reasoning models |
| Plugins | Tool collections passed to agents |

```bash
# Remove old, add new
dotnet remove package Microsoft.SemanticKernel
dotnet add package Microsoft.Agents.AI --prerelease
dotnet add package Microsoft.Agents.AI.Workflows --prerelease
```

**AutoGen → MAF** [^184^]:
- `AssistantAgent` → `Agent` (similar API)
- `RoundRobinGroupChat` → `GroupChatBuilder`
- `MagenticOneGroupChat` → `MagenticBuilder`
- Event-driven runtime → data-flow-based graph workflows
- Core concepts preserved; orchestration patterns rethought for graph execution

Both migrations have detailed guides with side-by-side code comparisons [^184^][^186^].

#### 12. Agent Memory Architecture

The pluggable memory system supports multiple backends [^137^][^222^][^227^]:

**Context Provider Interface**:
- `before_run()` — injects context before agent invocation
- `after_run()` — persists state after agent response

**Neo4j Agent Memory** provides three memory types [^221^][^222^]:
- **Short-term**: Conversation history (current session messages)
- **Long-term**: Entities, preferences, facts extracted from conversations
- **Reasoning**: Past tool usage patterns and reasoning traces

**Azure Managed Redis** for high-performance memory [^227^]:
- Thread/session state via `ChatMessageStore`
- Long-term memory via `ContextProviders` with Mem0 for intelligent extraction
- Vector store for semantic search

#### 13. DevUI and Developer Experience

DevUI is a browser-based local debugger [^223^][^222^]:

```csharp
// .NET: Add package and map endpoint
dotnet add package Microsoft.Agents.AI.DevUI

// In Program.cs
app.MapOpenAIResponses();
app.MapOpenAIConversations();
if (builder.Environment.IsDevelopment())
{
    app.MapDevUI(); // Serves at /devui
}
```

**Features**:
- Chain of thought visualization (reasoning → action → observation)
- Real-time conversation state monitoring
- Message flow between agents in multi-agent workflows
- Workflow topology diagrams
- Tool call inspection with arguments and results
- Streaming response viewing

**Aspire Integration** [^219^]:
```csharp
// Service discovery, health checks, distributed tracing
var apiService = builder.AddProject<Projects.ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

// OpenTelemetry wired through shared service defaults
builder.Services.AddOpenTelemetry()
    .AddSource("Microsoft.Extensions.AI")
    .AddSource("*Microsoft.Extensions.Agents*");
```

#### 14. Hosting and Deployment Patterns

**Local Development**:
- Direct execution via `InProcessExecution`
- DevUI for debugging
- Aspire dashboard for local observability

**Cloud Deployment**:
- **Foundry Agent Service**: Managed containers with automatic provisioning
- **Azure Container Apps**: Via `azd up` with Aspire
- **Azure Functions**: Azure Durable Functions integration (preview)

**Protocols for Hosted Agents** [^87^]:
- **Responses protocol**: OpenAI-compatible `/responses` endpoint
- **Invocations protocol**: Custom `/invocations` endpoint for structured messaging
- Both can be exposed simultaneously

#### 15. Version History

| Milestone | Date | Details |
|-----------|------|---------|
| Preview announcement | October 2025 | Initial public preview at .NET Conf 2025 [^199^] |
| Release Candidate | February 2026 | API surface stable, all v1.0 features complete [^186^] |
| v1.0 GA | April 2026 | Production release with backward compatibility guarantee [^137^] |
| Post-GA | Ongoing | DevUI, Skills, GitHub Copilot SDK, Claude Code SDK integrations [^137^] |

---

### Source Bibliography

[^26^] Microsoft Learn - Microsoft Agent Framework Overview (2026-04-06)
[^87^] Microsoft Learn - Deploy a hosted agent - Microsoft Foundry (2026-05-09)
[^88^] Microsoft Learn - Quickstart: Deploy your first hosted agent (2026-04-23)
[^89^] Traefik Blog - The Agent Framework Wars Are Over (2026-03-24)
[^90^] Medium - Developing AI Agents in .NET Web API using Microsoft Agent Framework (2026-03-18)
[^91^] Alibaba Cloud - AutoGen architecture evolution to MAF (2026-03-07)
[^92^] Dev.to - Building Your First AI Agent in C# with Microsoft Agent Framework (2026-02-08)
[^93^] CNBlogs - .NET AI ecosystem reconstruction with MAF (2026-02-02)
[^95^] Medium - Deploy Hosted Agents on Microsoft Foundry (2026-02-03)
[^134^] Microsoft DevBlogs - Microsoft Agent Framework Building Blocks Part 3 (2026-05-04)
[^135^] GitHub - Python: human-in-the-loop tool approval issue (2026-05-06)
[^136^] Cloud Wars - Microsoft Framework Supports Complex Workflows (2026-04-09)
[^137^] Microsoft DevBlogs - Microsoft Agent Framework Version 1.0 (2026-04-03)
[^138^] Microsoft Learn - Human-in-the-Loop with AG-UI (2026-04-01)
[^139^] Microsoft Learn - Observability - Microsoft Agent Framework (2026-04-01)
[^140^] Microsoft DevBlogs - A Tour of Handoff Orchestration Pattern (2026-05-08)
[^141^] Medium - Observability for Agent Framework apps on Azure (2026-04-26)
[^142^] Microsoft Learn - Configure tracing for AI agent frameworks (2026-03-27)
[^143^] Microsoft Learn - Using function tools with HITL approvals (2026-04-02)
[^144^] Microsoft Learn - Workflows Human-in-the-loop (2026-03-30)
[^145^] Microsoft Learn - Agent tracing in Microsoft Foundry (2026-03-27)
[^146^] Microsoft Learn - Magentic Orchestration (2026-03-13)
[^147^] Medium - The 5 AI Agent Orchestration Patterns by Microsoft (2026-02-05)
[^149^] Jamie Maguire - Microsoft Agent Framework: Implementing HITL (2025-12-06)
[^181^] GuruSup - Best Multi-Agent Frameworks in 2026 (2026-05-02)
[^182^] PE Collective - AI Agent Frameworks Compared (2026-04-06)
[^183^] Intuz - Top 5 AI Agent Frameworks in 2026 (2026-04-22)
[^184^] Microsoft Learn - AutoGen to MAF Migration Guide (2026-04-02)
[^186^] Microsoft DevBlogs - Migrate SK/AutoGen to MAF RC (2026-02-20)
[^187^] Microsoft Open Source Blog - Agent Governance Toolkit (2026-04-02)
[^188^] GitHub - Agent-Framework-Samples/00.ForBeginners (2025-09-26)
[^189^] DataCamp - CrewAI vs LangGraph vs AutoGen (2025-09-28)
[^190^] Microsoft Learn - Declarative Agents (2026-04-02)
[^191^] Microsoft Learn - Declarative Workflows (2026-03-11)
[^193^] Diagrid - Still Not Durable: MAF and Strands (2026-03-02)
[^195^] Dev.to - Agent Middleware in MAF 1.0 (2026-04-04)
[^196^] TechCommunity - Building Production-Ready Multi-Agent Systems (2025-11-25)
[^197^] GitHub - microsoft/agent-framework README (2026-05-09)
[^198^] Microsoft AI Adoption Journey PDF (2026-04-10)
[^199^] Microsoft DevBlogs - Introducing MAF (2025-10-02)
[^201^] GitHub - Semantic Kernel Docs: MAF Overview (2025-10-01)
[^216^] TechCommunity - AG-UI: Future of Agent-Driven UIs (2026-04-29)
[^217^] Microsoft Learn - AG-UI Integration with Agent Framework (2026-04-09)
[^218^] Microsoft DevBlogs - Local Models to Agent Workflows (2026-02-10)
[^219^] Microsoft Developer Blog - Interview Coach sample (2026-03-09)
[^221^] Neo4j Blog - Connected Context and Persistent Memory (2026-04-16)
[^222^] Neo4j Blog - Building AI Agent with Memory + MAF (2026-04-08)
[^223^] Microsoft DevBlogs - AG-UI, DevUI & OpenTelemetry Deep Dive (2025-12-03)
[^224^] Microsoft Learn - Neo4j Memory Provider (2026-04-01)
[^225^] Microsoft Learn - MagenticBuilder Python API (2025-12-12)
[^226^] Microsoft Learn - Magentic Agent Orchestration in SK (2025-05-22)
[^227^] TechCommunity - Supercharging AI Agents with Memory on Redis (2025-10-01)
[^228^] AG-UI Protocol Documentation (undated)
