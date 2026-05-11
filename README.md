# Expert Agents Prototype

A multi-agent development platform where "expert" agents own specific code repositories, communicate via A2A protocol, expose tools via MCP, and integrate with Claude Code, VS Code Copilot Chat, and a standalone web interface.

## Architecture

```
                    +---------------------------+
                    |        Claude Code         |
                    |  (via MCP stdio/HTTP)     |
                    +-------------+-------------+
                                  |
                    +-------------v-------------+
                    |    VS Code Copilot Chat   |
                    |  (@fhir-server, @health)  |
                    +-------------+-------------+
                                  |
                    +-------------v-------------+
+--------+          |      Web Chat (React)      |          +--------+
| User   +--------->|    http://localhost:5173   |<---------| User   |
+--------+          +-------------+-------------+          +--------+
                                  |
                    +-------------v-------------+
                    |    AgentHost (.NET 9)      |
                    |    http://localhost:5000   |
                    |                            |
    +---------------+---------------+   +--------+--------+
    |       A2A Protocol Layer     |   |   MCP Server     |
    |   JSON-RPC 2.0 / HTTP / SSE  |   |  Tool Discovery  |
    +---------------+---------------+   +--------+--------+
                    |                            |
    +---------------v---------------+   +--------v--------+
    |     Expert Agent Registry    |   | Code Search     |
    |                              |   | File Content    |
    |  +-----------------------+   |   | Architecture    |
    |  | FHIR Server Expert    |   |   | PR Guidance     |
    |  | (microsoft/fhir-server)|   |   +-----------------+
    |  +-----------------------+   |
    |  +-----------------------+   |
    |  | Healthcare Shared     |   |
    |  | Components Expert     |   |
    |  | (microsoft/healthcare-|   |
    |  |  shared-components)   |   |
    |  +-----------------------+   |
    +-------------------------------+
                    |
    +---------------v---------------+
    |   Code Index Service (mock)  |
    |   (Qdrant for production)    |
    +-------------------------------+
```

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 22+](https://nodejs.org/)
- [VS Code](https://code.visualstudio.com/) (for extension development)

### 1. Run the Backend

```bash
cd src/AgentHost
dotnet run
```

The AgentHost starts on `http://localhost:5000` with:
- A2A endpoints at `/.well-known/agent-card.json`, `/tasks/*`
- SignalR hub at `/hub/chat`
- MCP tools at `/mcp/tools`
- Swagger UI at `/swagger`

### 2. Run the Web Chat

```bash
cd web-chat
npm install
npm run dev
```

Opens on `http://localhost:5173`. Talk to expert agents via @-mentions.

### 3. Install the VS Code Extension

```bash
cd vscode-extension
npm install
npm run compile
```

Then in VS Code:
- Press F5 to open Extension Development Host
- In Copilot Chat, type `@fhir-server` or `@healthcare-components`

## Expert Agents

### FHIR Server Expert
Owns knowledge of `microsoft/fhir-server` with skills for:
- **Code Search**: Find implementation details across the FHIR server codebase
- **Architecture Q&A**: Deep technical questions about the R4 server, SQL Server/Cosmos backends, $export, SMART on FHIR
- **PR Guidance**: Step-by-step help creating PRs with proper branch naming and test requirements

### Healthcare Shared Components Expert
Owns knowledge of `microsoft/healthcare-shared-components` with skills for:
- **Code Search**: Find components, utilities, and patterns in the shared library
- **Architecture Q&A**: Retry patterns, blob storage abstraction, Mediator pattern, configuration management
- **PR Guidance**: Component-specific contribution guidelines

Both agents can cross-refer when questions span repositories.

## A2A Protocol Implementation

This prototype implements the [Agent-to-Agent (A2A) Protocol](https://github.com/a2aproject) for agent discovery and communication.

### Agent Cards
Each agent publishes metadata at `/.well-known/agent-card.json`:

```json
{
  "name": "fhir-server-expert",
  "description": "Expert agent for Microsoft FHIR Server",
  "version": "1.0.0",
  "url": "http://localhost:5000",
  "capabilities": { "streaming": true },
  "skills": [
    {
      "name": "code-search",
      "description": "Search the FHIR Server codebase",
      "inputModes": ["text"],
      "outputModes": ["text", "file"]
    }
  ]
}
```

### Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/.well-known/agent-card.json` | Agent metadata (Agent Card) |
| POST | `/tasks/send` | Send task synchronously |
| POST | `/tasks/sendSubscribe` | Send task with SSE streaming |
| GET | `/tasks/{taskId}` | Get task status |
| POST | `/tasks/{taskId}/cancel` | Cancel task |

### Task Lifecycle

```
Submitted -> Working -> Completed
                    -> Failed
                    -> Canceled
                    -> InputRequired
```

## MCP (Model Context Protocol) Tools

The AgentHost exposes MCP-compatible tools for external integration:

| Tool | Description |
|------|-------------|
| `search_code` | Semantic search across indexed repositories |
| `get_file_content` | Retrieve specific file content by path |
| `explain_architecture` | Get component architecture overview |
| `create_pr` | PR creation guidance with branch naming |
| `list_agents` | Discover available expert agents |
| `ask_agent` | Send a question to a specific agent |

Connect to MCP tools via HTTP POST to `/mcp/tools/call` with standard JSON-RPC 2.0 envelope.

## VS Code Extension

The extension integrates expert agents as Copilot Chat participants:

- **@fhir-server** — Route questions to the FHIR Server Expert
- **@healthcare-components** — Route questions to the Healthcare Components Expert

Features:
- Streaming responses with markdown rendering
- Follow-up question suggestions
- Works offline with mock responses when backend is unavailable
- Configurable backend URL via settings

## Claude Code Integration

To connect Claude Code to the expert agents via MCP:

```bash
# Add MCP server
claude mcp add expert-agents \
  --transport http \
  --url http://localhost:5000/mcp

# Or use stdio transport with a bridge
claude mcp add expert-agents \
  --transport stdio \
  --command "dotnet run --project /path/to/AgentHost"
```

Then in Claude Code:
- Use tools like `search_code`, `ask_agent`, `explain_architecture`
- Ask "@fhir-server how does custom search work?"
- The agent context is automatically injected via MCP

## Project Structure

```
expert-agents-prototype/
├── src/
│   └── AgentHost/              # ASP.NET Core backend (.NET 9)
│       ├── Program.cs          # Entry point, DI, middleware
│       ├── A2A/                # A2A protocol implementation
│       │   ├── A2AModels.cs           # AgentCard, AgentTask, Message, Part
│       │   ├── AgentCardProvider.cs   # Hardcoded agent cards
│       │   ├── AgentTaskStore.cs      # In-memory task storage
│       │   └── A2AEndpoints.cs        # A2A HTTP endpoints
│       ├── Agents/             # Expert agent implementations
│       │   ├── IExpertAgent.cs
│       │   ├── FhirServerAgent.cs
│       │   ├── HealthcareComponentsAgent.cs
│       │   └── AgentRegistry.cs
│       ├── Hubs/               # SignalR real-time hub
│       │   └── ChatHub.cs
│       ├── MCP/                # MCP tool server
│       │   └── CodeMcpServer.cs
│       └── Services/           # Shared services
│           └── MockCodeIndexService.cs
├── web-chat/                   # React chat interface
│   ├── src/
│   │   ├── App.tsx
│   │   ├── store/chatStore.ts
│   │   ├── services/agentClient.ts
│   │   └── components/
│   │       ├── Sidebar.tsx
│   │       ├── ChatInterface.tsx
│   │       ├── MessageList.tsx
│   │       ├── AgentMention.tsx
│   │       └── StreamingText.tsx
│   ├── package.json
│   └── vite.config.ts
├── vscode-extension/           # VS Code Copilot Chat extension
│   ├── src/
│   │   ├── extension.ts
│   │   ├── types.ts
│   │   ├── agentClient.ts
│   │   ├── fhirServerParticipant.ts
│   │   └── healthcareComponentsParticipant.ts
│   └── package.json
├── docker-compose.yml
├── ExpertAgents.sln
└── README.md
```

## Technology Stack

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| Backend | .NET 9 + ASP.NET Core | Microsoft stack, high performance, Minimal APIs |
| Agent Framework | Custom (A2A + MCP) | Direct protocol implementation for clarity |
| Protocols | A2A + MCP | Open standards for agent interoperability |
| Real-time | SignalR | Native .NET WebSocket support |
| Frontend | React 19 + TypeScript + Tailwind | Modern, type-safe, utility-first CSS |
| State | Zustand | Lightweight, no boilerplate |
| VS Code Ext | TypeScript + VS Code API | Native Copilot Chat integration |
| Vector DB | Qdrant (production) | Open-source, native .NET client, hybrid search |
| Embeddings | voyage-code-3 (production) | Best-in-class for code retrieval |
| Container | Docker + .NET 9 images | Cross-platform deployment |

## Production Enhancements

This prototype demonstrates core concepts. For production:

1. **Code Indexing**: Replace `MockCodeIndexService` with Qdrant + AST-based chunking using TreeSitter.DotNet
2. **LLM Integration**: Add Azure OpenAI or local model inference for real AI responses
3. **Authentication**: Add Microsoft Entra ID, OAuth 2.0, mTLS for A2A
4. **Persistence**: Replace in-memory stores with Redis/Dapr state management
5. **Orchestration**: Deploy on Azure Container Apps with Dapr sidecars
6. **Observability**: Add OpenTelemetry, Azure Monitor, distributed tracing
7. **Agent Memory**: Add graph-native memory (Neo4j) for cross-session context

## Research Foundation

This prototype is built on comprehensive research across:

- **A2A Protocol**: Google's open standard (v1.0), Linux Foundation governed
- **MCP**: Anthropic's Model Context Protocol for tool integration
- **Microsoft Agent Framework**: Unified Semantic Kernel + AutoGen (GA Oct 2025)
- **VS Code Chat API**: Production-stable participant API (v1.93+)
- **Code RAG**: voyage-code-3 embeddings, Qdrant vector search, AST chunking

See `/mnt/agents/output/research/` for full research reports (12 dimensions, 6 wide-exploration facets).

## License

MIT License - Demonstration prototype for educational purposes.

## Secrets / Configuration

### Local Development

AgentHost uses [ASP.NET Core User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for local secret storage — secrets are never stored in source control.

```bash
cd src/AgentHost
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "<your-key>"
dotnet user-secrets set "AzureOpenAI:Endpoint" "<your-endpoint>"
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-key>"
```

### Cloud (Azure Container Apps)

In production, secrets are provided via **ACA Key Vault secret bindings** (to be wired in Phase 7). No secrets should be committed to source control. The `appsettings.Development.json` file is git-ignored.

**Never commit API keys, connection strings, or other secrets to this repository.**
