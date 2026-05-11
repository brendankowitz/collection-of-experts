## Facet: Multi-Agent Orchestration, Hosting & Deployment Patterns

### Key Findings
- Five dominant orchestration patterns have emerged for production multi-agent systems: Orchestrator-Worker (centralized), Swarm (decentralized), Mesh (peer-to-peer), Hierarchical (tree-structured), and Pipeline (sequential stage-based) [^271^]
- Dapr Agents provides a purpose-built framework combining Dapr's service invocation, pub/sub messaging, and workflow-based orchestration with virtual actors for resilient, stateful agent interactions [^289^]
- Azure Container Apps offers first-class built-in Dapr support with automatic sidecar injection, managed mTLS, and KEDA-based event-driven autoscaling [^323^]
- Agent discovery is converging on three strategies: well-known URI (RFC 8615), curated registry/Agent Cards (A2A protocol), and private configuration [^313^]
- Microsoft Foundry Hosted Agents represent an emerging "agent-centric hosting model" with built-in OpenTelemetry, managed lifecycle, and framework support (LangGraph, Microsoft Agent Framework) [^291^]
- AGNTCY (Linux Foundation project, donated by Cisco) is building foundational observability infrastructure for multi-agent systems, including an Observe SDK, multi-agentic schema, and Metrics Compute Engine [^298^]
- Microsoft Entra Agent ID is now GA, bringing first-class identity and access management to AI agents with Zero Trust principles and specialized OAuth flows [^312^]
- Dapr's DurableAgent pattern enables long-running, fault-tolerant execution with persistent workflow state management across sessions and failures [^290^]
- Kubernetes health probes (liveness, readiness, startup) form the foundation for agent lifecycle management, with distinct endpoints for each probe type [^314^][^316^]
- A2A protocol security requires enterprise-grade controls: mTLS, OAuth 2.0/OIDC, API keys, with agents declaring supported auth schemes in their Agent Cards [^81^][^295^]

---

### Major Players & Sources
- **Microsoft**: Azure Container Apps (Dapr integration), Azure AI Foundry (Prompt/Workflow/Hosted agents), Microsoft Entra Agent ID, Microsoft Agent Framework [^291^][^312^][^310^]
- **Google**: A2A (Agent-to-Agent) Protocol, Agent Cards, well-known discovery endpoints [^313^][^319^]
- **CNCF/Dapr**: Dapr project (incubating), Dapr Agents framework, pub/sub, service invocation, actors, workflows [^270^][^289^]
- **Cisco/Outshift**: AGNTCY project (Linux Foundation), Observe SDK, multi-agent observability standards [^298^][^324^]
- **Splunk**: AI Agent Monitoring, integration with AGNTCY Metrics Compute Engine [^325^][^332^]
- **Oracle**: Enterprise multi-agent platform with Agent Card discovery and orchestration [^275^]
- **Palo Alto Networks**: A2A protocol security analysis and threat research [^81^]
- **AWS**: A2A Agent Registry (serverless, Lambda + Bedrock), KEDA scaling patterns [^277^]
- **Diagrid**: Dapr expertise, Dapr Agents building blocks [^285^][^301^]

---

### Trends & Signals
- **Shift to agent-centric hosting**: Microsoft Foundry Hosted Agents represent a move from "hosting containers that happen to run agents" to "hosting agents as first-class entities" with native abstractions for conversations, tool calls, and observability [^291^]
- **Dapr Agents emergence**: The official Dapr Agents framework (announced March 2025) combines deterministic workflows with event-driven interactions, positioning Dapr as a foundational layer for agent infrastructure [^289^][^301^]
- **Standardized agent discovery**: The A2A protocol's Agent Card mechanism (`/.well-known/agent-card.json`) is gaining traction as the de facto standard, with registries emerging from AWS, Oracle, and open-source projects [^277^][^275^][^287^]
- **Enterprise observability convergence**: Microsoft and Cisco are collaborating to extend OpenTelemetry semantic conventions for multi-agent systems, creating vendor-neutral standards for agent tracing [^298^][^324^]
- **Zero Trust for agents**: Microsoft Entra Agent ID extends Zero Trust to AI agents with specialized OAuth flows, complementing the A2A protocol's authentication model [^312^][^295^]
- **Kubernetes-native deployment**: Dapr Agents explicitly positions as "Kubernetes-Native" with platform-ready access scopes and declarative resources [^292^]
- **Serverless agent hosting**: Azure Container Apps' scale-to-zero with Dapr pub/sub + KEDA scalers enables cost-effective event-driven agent architectures [^323^][^331^]

---

### Controversies & Conflicting Claims
- **Managed vs. self-hosted agents**: Microsoft presents Foundry Hosted Agents as the "sweet spot" between control and simplicity, but acknowledges AKS is needed for "strict compliance, multi-cluster deployment, or complex networking" [^291^]. Self-hosting on ACA offers more control but requires manual observability and self-managed conversation state.
- **Agent Card security**: The A2A well-known endpoint discovery pattern is simple but risks exposing sensitive capability metadata publicly; the spec recommends authenticated extended agent cards for sensitive information [^320^][^313^]
- **Dapr limitations in ACA**: Not all Dapr capabilities are available -- configuration spec, some sidecar annotations, and non-GA components are unsupported. Actor reminders require `minReplicas >= 1`, breaking pure scale-to-zero for stateful agents [^23^]
- **Sidecar resource overhead**: Dapr's sidecar pattern adds a container per pod, raising resource concerns at scale. AGNTCY's SLIM messaging promises "secure, low-latency, interactive messaging" as a lighter alternative [^334^]
- **A2A protocol security gaps**: Palo Alto Networks identifies multiple attack vectors including Agent Card context poisoning, agent impersonation, and infrastructure attacks that the base protocol does not fully mitigate [^81^]

---

### Recommended Deep-Dive Areas
- **Dapr Workflows for agent orchestration**: The fan-out/fan-in, chaining, and actor patterns have deep implications for multi-agent reliability -- worth exploring Dapr's durable execution guarantees in detail
- **Azure AI Foundry Hosted Agents hosting adapter**: The framework abstraction layer (`from_langgraph`, `from_agent_framework`) is a novel approach that could standardize agent deployment patterns
- **AGNTCY Observe SDK and multi-agentic schema**: Cisco's open-source observability toolkit with LLM-as-a-judge evaluation represents a significant advancement in agent monitoring
- **Microsoft Entra Agent ID + A2A integration**: How enterprise identity combines with protocol-level authentication for cross-domain agent trust
- **KEDA scaling for Dapr agent workloads**: The intersection of event-driven autoscaling and agent pub/sub patterns has major cost/performance implications

---

### Detailed Notes

#### 1. Multi-Agent Orchestration Patterns

Five main orchestration patterns have emerged for production multi-agent systems [^271^][^274^]:

**1. Orchestrator-Worker (Hub-and-Spoke)**
- One "controller" agent delegates tasks to sub-agents
- Great for simple workflows but limited scalability
- Example: Planner -> Researcher -> Writer -> QA
- Works well when tasks are clearly decomposable

**2. Swarm**
- Decentralized, emergent coordination
- Agents communicate peer-to-peer without central controller
- Good for exploration and emergent problem-solving
- Harder to debug and guarantee outcomes

**3. Mesh (Peer-to-Peer)**
- Agents communicate via message brokers (Kafka, Redis Streams, Dapr pub/sub)
- Best for distributed teams or long-running reasoning chains
- Example: `ResearchAgent -> Kafka -> AnalysisAgent -> APIAgent -> StorageAgent`
- Dapr's pub/sub building block directly enables this pattern [^270^]

**4. Hierarchical**
- Tree-structured delegation
- Director agents manage specialized agent pods (e.g., Finance, Legal, Ops)
- Each pod deploys as a containerized microservice
- Ideal for multi-tenant AI SaaS [^274^]

**5. Pipeline**
- Sequential stage-based processing
- MapReduce-style parallelism for efficiency
- Producer-Reviewer loops for quality assurance
- Easy to reason about and debug

Dapr supports both choreography and orchestration: "Dapr Agents support both deterministic workflows and event-driven interactions. Built on Dapr Workflows, which leverage Dapr's virtual actors underneath, agents function as self-contained, stateful entities" [^289^].

#### 2. Dapr for Agent-to-Agent Communication

Dapr provides multiple communication patterns for agents [^270^][^283^]:

**Pub/Sub Messaging**
- Event-driven, decoupled architecture
- Asynchronous communication for scalability and modularity
- Agents react dynamically to events
- Combined with workflow capabilities, agents can collaborate through event streams while participating in larger orchestrated workflows

**Service Invocation**
- Direct request/response between agents
- Agents can invoke other agents as tools within a reasoning loop
- Cross-app routing handled transparently by Dapr sidecar

**Actors (Virtual Actors)**
- Stateful agent entities with automatic activation/deactivation
- Turn-based access model simplifies concurrency
- Dapr distributes actor instances throughout the cluster
- Automatic migration to healthy nodes on failure

**Workflows**
- Durable, long-running execution with checkpointing
- Fan-out/fan-in, chaining, and human-in-the-loop patterns
- Automatic retry and recovery mechanisms
- Deterministic execution guarantees

The Dapr sidecar architecture means "all the infrastructure concerns -- persistence, messaging, reliability -- are handled by the Dapr runtime running alongside your application" [^301^].

#### 3. Azure Container Apps Dapr Integration

Azure Container Apps provides first-class built-in Dapr support [^323^][^23^]:

**How It Works**
- Dapr is enabled per Container App with a simple flag (`--enable-dapr`)
- Components are defined at the ACA Environment level
- The Dapr sidecar is automatically injected and configured
- mTLS between services is handled by ACA's internal networking
- Scaling can be triggered by Dapr pub/sub queue depth via KEDA

**Supported Building Blocks**
- Service invocation (HTTP/gRPC via sidecar on ports 3500/50001)
- Pub/sub messaging
- State store
- Bindings
- Secrets
- Configuration

**Scaling Integration**
- ACA's built-in KEDA support enables auto-scaling consumers based on pub/sub queue depth
- Define scaling rules declaratively in Bicep [^331^]
- Scale-to-zero supported for event-driven workloads

**Limitations**
- Dapr Configuration spec capabilities are not supported
- Some Dapr sidecar annotations are not available
- Only GA, Tier 1, or Tier 2 APIs and components are supported
- Actor reminders require `minReplicas >= 1` [^23^]
- Dapr is not supported for jobs

ACA represents "one of the fastest ways to get Dapr microservices into production on Azure" because "there is no need to manually configure the sidecar, manage certificates, or run the Dapr control plane - ACA handles it all" [^323^].

#### 4. Agent Discovery Mechanisms

Agent discovery is converging around three strategies [^313^][^310^]:

**Well-Known URI**
- Standard path: `https://{domain}/.well-known/agent-card.json` (RFC 8615)
- Best for public or domain-controlled discovery
- Client performs HTTP GET to discover agent capabilities
- Microsoft Agent Framework provides `A2ACardResolver` for this [^310^]

**Curated Registry**
- Central catalog indexes AgentCards
- Useful for enterprise governance, policy filtering, and capability search
- AWS A2A Agent Registry: serverless with semantic search via Bedrock + S3 Vectors [^277^]
- Oracle's platform: Orchestrator polls registry every few minutes for new agents [^275^]
- Microsoft Entra Agent ID: enterprise directory with lifecycle and governance [^287^]

**Private Configuration**
- Client learns card URLs via internal config, secrets management, or proprietary APIs
- Simple for fixed topologies, less flexible for dynamic ecosystems
- Best for tightly-coupled systems or development scenarios

**Agent Card Structure**
An AgentCard typically includes [^319^][^313^]:
- Identity and provider info (name, version, description)
- Service endpoint(s)
- Capabilities (streaming, push notifications, extensions)
- Authentication requirements
- Skill metadata (what the agent can do)
- Protocol version compatibility

**Security Recommendations for Discovery**
- Protect card endpoints with mTLS, OAuth, network policy
- Use authenticated extended agent cards for sensitive capability details
- Avoid embedding static credentials in card payloads
- Use identity-aware responses when exposing different capability levels [^320^]

#### 5. Scaling Patterns for Agent Workloads

Agent workload scaling operates at multiple dimensions [^273^][^278^][^323^]:

**Horizontal Scaling (HPA/KEDA)**
- Scale based on CPU/memory utilization (HPA)
- Scale based on custom metrics: queue depth, event count, request rate (KEDA)
- Scale-to-zero for event-driven agent consumers
- Dapr pub/sub queue depth is a natural scaling signal for agent workloads

**Vertical Scaling (VPA)**
- Adjust CPU and memory allocations per pod
- Best for recurring adjustments to resource allocations
- Usually requires pod restart

**ACA-Specific Scaling**
- HTTP traffic-based scaling
- CPU or memory usage triggers
- Azure Storage Queue depth
- KEDA event-driven triggers (Service Bus, Event Hubs, Kafka) [^328^][^331^]

**Predictive Scaling**
- Use historical data to predict resource needs
- ML-based forecasting to scale before traffic spikes
- Requires custom metrics provider (Prometheus + custom exporter)

**Key Insight**: For event-driven agents, the right scaling signal is message queue depth rather than CPU. "Custom metrics can help by aligning your capacity with business logic rather than hardware side effects" [^278^].

#### 6. Agent Persistence and State Management

Agent state management has multiple layers [^290^][^292^][^301^]:

**Ephemeral Context**
- In-memory KV stores (Redis)
- Fast but lost on restart

**Persistent Workflow State (Dapr)**
- Dapr Workflows persist every agent interaction with LLMs and tools into a durable state store
- Automatic recovery and continuation after agent restarts
- Checkpointing at each workflow step
- Supports complex multi-agent collaboration patterns

**Agent Memory Configuration**
```python
memory = AgentMemoryConfig(
    store=ConversationDaprStateMemory(
        store_name="conversationstore",
        session_id="travel-session",
    )
)
```

**State Best Practices**
- Actor state should remain small and focused
- Minimize state writes (each is a remote call)
- Offload heavy tasks to background services rather than blocking actors [^285^]
- Never rely on in-memory fields for important state

**Dapr Agent State Features**
- `AgentStateConfig` for workflow state storage
- `ConversationDaprStateMemory` for conversation history
- `AgentRegistryConfig` for agent registration state
- Transactional state stores for consistent state operations [^290^]

#### 7. Security Models for Multi-Agent Systems

Security spans identity, authentication, authorization, and runtime isolation [^295^][^81^][^299^]:

**A2A Protocol Security**
- HTTPS for all communications (TLS encryption)
- Agents declare supported authentication schemes in Agent Cards (OAuth 2.0, OIDC, API keys, mTLS)
- Enterprise-grade RBAC
- Zero-trust governance model

**Microsoft Entra Agent ID**
- First-class identity for AI agents (now GA)
- Extends Zero Trust principles to AI workloads
- Purpose-built identity constructs
- Specialized OAuth flows for agent-to-agent authentication [^312^]

**Zero Trust Implementation**
- Every agent gets its own service account identity
- Explicit authentication enforced on every call
- Independent secondary authorization on resource servers
- Token downscoping following least privilege [^299^][^295^]

**Security Issues & Mitigations** [^81^]:
| Issue | Mitigation |
|-------|-----------|
| Authentication/Authorization | Strict credential validation, granular backend authorization checks |
| Agent Card Poisoning | Rigorous prompt sanitization, context isolation, strict validation |
| Agent Impersonation | Secure identity verification, anomaly detection |
| Infrastructure Attacks | Input sanitization, sandboxed execution, resource limits |
| Application/Logic Attacks | Rate limits, containerization, input validation |

**OAuth Transaction Tokens**
- Cross-domain token exchange preserves original requester context
- Downscoping: access rights decrease at each downstream step
- Prevents privilege abuse by master agents [^299^]

#### 8. Azure AI Foundry Agent Service vs. Self-Hosting

Microsoft offers a spectrum of agent hosting options [^291^][^288^]:

| Option | Control | Complexity | Best For |
|--------|---------|-----------|----------|
| Azure Container Apps | High | Medium | Custom orchestration, full container control |
| Azure Kubernetes Service | Very High | High | Enterprise scale, strict compliance, multi-cluster |
| Azure App Service | Medium | Low | Simple web-based agents |
| Azure Functions | Low | Low | Event-driven, short tasks |
| Foundry Prompt Agents | Low | Very Low | Rapid prototyping, simple tasks |
| Foundry Hosted Agents | Medium | Very Low | Custom frameworks, managed infrastructure |

**Foundry Hosted Agents (Preview)**
- Containerized agentic AI applications running on Foundry Agent Service
- Purpose-built hosting model for agentic workloads
- Key capabilities [^291^]:
  - Agent-native abstractions (conversations, responses, tool calls as first-class concepts)
  - Managed lifecycle (create, start, update, stop, delete via API)
  - Built-in OpenTelemetry traces, metrics, and logs
  - Framework support: LangGraph, Microsoft Agent Framework, custom code
  - Hosting Adapter for protocol translation

**Hosting Adapter**
The "secret sauce" that bridges frameworks to Foundry:
- Converts Foundry Responses API <-> framework format
- Manages message serialization and conversation history
- Provides Server-Sent Events for streaming
- OpenTelemetry TracerProvider, MeterProvider, LoggerProvider
- Local testing on localhost:8088

**Key Trade-off**: "For teams that want the simplicity of managed infrastructure with the flexibility of custom agent code, Hosted Agents represent the sweet spot" [^291^]. However, note "preview SLAs, no private networking yet."

#### 9. Monitoring & Observability for Agent Systems

Multi-agent observability requires tracing beyond traditional request/response [^289^][^293^][^298^]:

**OpenTelemetry for Agents**
- Distributed tracing across agent invocations, LLM calls, and tool executions
- GenAI semantic conventions for LLM calls, token usage, costs
- Context propagation with baggage for session affinity across agents
- Unified metrics, traces, and logs collection [^298^]

**AGNTCY Observability Stack** [^325^][^334^]
The Linux Foundation project provides:
- **Observe SDK**: Instrumentation layer for LLMs and agentic systems
- **Multi-agentic schema**: Standardized telemetry schema for agent roles, tool usage, inter-agent communication
- **Metrics Compute Engine (MCE)**: LLM-as-a-judge for quality metrics (relevance, hallucination, bias)
- **Translator**: Normalizes third-party telemetry schemas to OTel
- **Telemetry Hub**: Centralized normalized telemetry from diverse sources

**Key Metrics to Track**
- Quantitative: latency, error rates, token consumption, resource utilization
- Qualitative: accuracy, coherence, consistency (via MCE evaluation)
- Agent-specific: tool call counts, agent collaboration patterns, conversation length

**Auto-instrumentation**
- Supports 40+ AI frameworks including LangChain, LlamaIndex, CrewAI, OpenAI Agents SDK
- Sampling strategies reduce telemetry costs while maintaining debug coverage
- "Teams with stronger AI observability and evaluation practices report 2.2x better reliability" [^289^]

**Splunk AI Agent Monitoring**
- Native integration with AGNTCY MCE (GA February 2026)
- Correlates AI quality metrics with operational metrics
- Detects hallucination incidents, factual accuracy issues
- Connects performance to business KPIs [^332^]

#### 10. Agent Lifecycle Management

Agent lifecycle follows Kubernetes patterns with Dapr extensions [^314^][^316^][^317^]:

**Kubernetes Health Probes**
- **Startup Probe**: Determines if application initialization is complete. Disables liveness/readiness until success. Prevents premature restarts for slow-starting agents. Use `failureThreshold: 30, periodSeconds: 10` for 5-minute startup window.
- **Liveness Probe**: "Is my application alive?" Triggers container restart on failure. Keep simple -- only detect unrecoverable states (deadlocks, memory leaks). Higher failure thresholds to avoid restart loops.
- **Readiness Probe**: "Is my application ready to serve traffic?" Removes pod from service endpoints on failure. Check critical dependencies (database, downstream services). More aggressive checking acceptable.

**Best Practices for Agents**
- Use separate endpoints: `/healthz/live`, `/healthz/ready`
- Readiness checks downstream service health; liveness only checks process responsiveness [^314^]
- Include startup probes for agents with long initialization (model loading, tool registration)
- Never implement the same endpoint for both probes
- Track health check metrics in monitoring system [^314^]

**Dapr Agent Lifecycle**
- `AgentRunner` manages agent lifecycle: `runner.run()`, `runner.subscribe()`, `runner.serve()`
- `DurableAgent` provides automatic retry, recovery, and deterministic execution
- Virtual actors activate on demand, deactivate after inactivity
- State outlives object lifetime, stored in configured state provider [^276^]
- Workflows persist checkpoint state after each step, enabling recovery from crashes

**Foundry Hosted Agents Managed Lifecycle**
- Create, start, update, stop, delete via API calls
- Hosting Adapter handles protocol translation automatically
- Built-in graceful shutdown support via runner patterns
- Scale-to-zero capability for cost optimization

---

### Source Index

| Citation | Source | URL |
|----------|--------|-----|
| [^23^] | Azure Container Apps Dapr Overview | https://learn.microsoft.com/en-us/azure/container-apps/dapr-overview |
| [^81^] | Palo Alto Networks A2A Security Guide | https://live.paloaltonetworks.com/t5/community-blogs/safeguarding-ai-agents-an-in-depth-look-at-a2a-protocol-risks/ba-p/1235996 |
| [^270^] | Dapr Agents - Why Dapr | https://docs.dapr.io/developing-ai/dapr-agents/dapr-agents-why/ |
| [^271^] | Agent Orchestration Patterns (GuruSup) | https://gurusup.com/blog/agent-orchestration-patterns |
| [^272^] | Multi-Agent Systems with A2A Protocol | https://medium.com/@yusufbaykaloglu/multi-agent-systems-orchestrating-ai-agents-with-a2a-protocol |
| [^273^] | Kubernetes Autoscaling Docs | https://kubernetes.io/docs/concepts/workloads/autoscaling/ |
| [^274^] | AI Agent Architecture Patterns 2025 | https://nexaitech.com/multi-ai-agent-architecutre-patterns-for-scale/ |
| [^275^] | Oracle Dynamic Multi-Agent Enterprise Platform | https://blogs.oracle.com/ai-and-datascience/building-a-dynamic-multi-agent-enterprise-platform |
| [^276^] | Dapr Actors Overview | https://docs.dapr.io/developing-applications/building-blocks/actors/actors-overview/ |
| [^277^] | AWS A2A Agent Registry | https://github.com/awslabs/a2a-agent-registry-on-aws |
| [^278^] | Datadog Custom Metrics Scaling | https://www.datadoghq.com/blog/autoscaling-custom-metrics/ |
| [^281^] | 5 Multi-Agent Orchestration Patterns (YouTube) | https://www.youtube.com/watch?v=l_i7icCA56c |
| [^283^] | Dapr Agents Core Concepts | https://docs.dapr.io/developing-ai/dapr-agents/dapr-agents-core-concepts/ |
| [^285^] | Understanding Dapr Actors for AI Agents | https://www.diagrid.io/blog/understanding-dapr-actors-for-scalable-workflows-and-ai-agents |
| [^287^] | AI Agent Registry Survey (arxiv) | https://arxiv.org/html/2508.03095v1 |
| [^288^] | Foundry Agent Service Overview | https://learn.microsoft.com/en-us/azure/foundry/agents/overview |
| [^289^] | CNCF Announcing Dapr AI Agents | https://www.cncf.io/blog/2025/03/12/announcing-dapr-ai-agents/ |
| [^290^] | Dapr Agents Core Concepts (Detailed) | https://docs.dapr.io/developing-ai/dapr-agents/dapr-agents-core-concepts/ |
| [^291^] | Microsoft Foundry Hosted Agents Blog | https://devblogs.microsoft.com/all-things-azure/hostedagent/ |
| [^292^] | Dapr Agents Introduction | https://docs.dapr.io/developing-ai/dapr-agents/dapr-agents-introduction/ |
| [^293^] | Groundcover AI Agent Observability Guide | https://www.groundcover.com/learn/observability/ai-agent-observability |
| [^295^] | Zero Trust A2A with ADK in Cloud Run | https://medium.com/google-cloud/implementing-zero-trust-a2a-with-adk-in-cloud-run-243aa4fb98ad |
| [^296^] | A2A Protocol Explained | https://onereach.ai/blog/what-is-a2a-agent-to-agent-protocol/ |
| [^298^] | Cisco AGNTCY Multi-Agent Observability | https://outshift.cisco.com/blog/ai-ml/ai-observability-multi-agent-systems-opentelemetry |
| [^299^] | IETF A2A Security Requirements Draft | https://datatracker.ietf.org/doc/draft-ni-a2a-ai-agent-security-requirements/ |
| [^300^] | Safeguarding Sensitive Data in Multi-Agent Systems | https://arxiv.org/html/2505.12490v1 |
| [^301^] | Building Effective Dapr Agents | https://www.diagrid.io/blog/building-effective-dapr-agents |
| [^310^] | Microsoft A2A Agent Framework Docs | https://learn.microsoft.com/en-us/agent-framework/agents/providers/agent-to-agent |
| [^311^] | Dapr Agents Patterns | https://docs.dapr.io/developing-ai/dapr-agents/dapr-agents-patterns/ |
| [^312^] | Microsoft Entra Agent ID GA | https://learn.microsoft.com/en-us/entra/agent-id/whats-new-agent-id |
| [^313^] | A2A Agent Discovery Docs | https://agent2agent.info/docs/topics/agent-discovery/ |
| [^314^] | Health Checks Best Practices | https://oneuptime.com/blog/post/2026-02-09-health-checks-liveness-vs-readiness/view |
| [^315^] | Dapr Workflow Patterns | https://docs.dapr.io/developing-applications/building-blocks/workflow/workflow-patterns/ |
| [^316^] | Kubernetes Probe Configuration | https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/ |
| [^317^] | Kubernetes Probe Debugging | https://resolve.ai/glossary/how-to-debug-kubernetes-probe-issues |
| [^319^] | Behind the Scenes of A2A Protocol | https://billtcheng2013.medium.com/behind-the-scenes-of-agent2agent-protocol-44a5c29f5389 |
| [^320^] | A2A Agent Discovery GitHub | https://github.com/a2aproject/A2A/blob/main/docs/topics/agent-discovery.md |
| [^321^] | Application Identity Security Zero Trust | https://www.appgovscore.com/blog/integrating-application-identity-security-into-your-zero-trust-framework |
| [^322^] | Floki AI Agentic Workflow with Dapr | https://blog.openthreatresearch.com/floki-building-an-ai-agentic-workflow-engine-dapr/ |
| [^323^] | Dapr with Azure Container Apps Serverless | https://oneuptime.com/blog/post/2026-03-31-dapr-how-to-use-dapr-with-azure-container-apps-serverless/view |
| [^324^] | Microsoft and Cisco Agentified Azure | https://www.sdxcentral.com/analysis/how-microsoft-and-cisco-agentified-azure/ |
| [^325^] | AGNTCY and Splunk AI Observability | https://outshift.cisco.com/blog/ai-ml/splunk-and-agntcy-power-ai-monitoring |
| [^326^] | Dapr and .NET with ACA | https://gokhan-gokalp.com/building-microservices-by-using-dapr-and-net-with-minimum-effort-02-azure-container-apps/ |
| [^327^] | ACA Auto Scaling with KEDA Part 11 | https://bitoftech.net/2022/09/22/azure-container-apps-auto-scaling-with-keda-part-11/ |
| [^328^] | ACA Auto Scaling with KEDA Module 9 | https://azure.github.io/aca-dotnet-workshop/aca/09-aca-autoscale-keda/ |
| [^329^] | Sidecar Pattern in Service Meshes | https://lukasniessen.medium.com/the-sidecar-pattern-why-every-major-tech-company-runs-proxies-on-every-pod-8138d79c597a |
| [^331^] | Scale Dapr Apps with KEDA Scalers | https://learn.microsoft.com/en-us/azure/container-apps/dapr-keda-scaling |
| [^332^] | Splunk AI Agent Monitoring | https://www.splunk.com/en_us/blog/observability/monitor-llm-and-agent-performance-with-ai-agent-monitoring-in-splunk-observability-cloud.html |
| [^334^] | Cisco AGNTCY Architecture (PDF) | https://www.ciscolive.com/c/dam/r/ciscolive/emea/docs/2026/pdf/BRKETI-1009.pdf |
| [^335^] | AGNTCY.org | https://agntcy.org/ |
| [^336^] | Azure Functions KEDA on Container Apps | https://learn.microsoft.com/en-us/azure/container-apps/functions-keda-mappings |
