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
- MCP endpoints at `/mcp` and `/mcp/health`
- Swagger UI at `/swagger`

### 2. Run the Web Chat

```bash
cd web-chat
npm install
npm run dev
```

Opens on `http://localhost:5173`. Talk to expert agents via @-mentions.

### LLM configuration

`AgentHost` now resolves chat providers through `AgentHost.Llm` and `Microsoft.Extensions.AI`.
By default the app uses the mock provider, but you can configure `Llm` settings in `src/AgentHost/appsettings.json`
or `appsettings.Development.json` to switch specific agents to OpenAI, Azure OpenAI, Anthropic, or Ollama.

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

The AgentHost exposes nine MCP-compatible tools for external integration over `POST /mcp` and `GET /mcp`:

| Tool | Description |
|------|-------------|
| `search_code` | Search indexed repositories for matching code snippets |
| `get_file_content` | Retrieve full file content from a repository |
| `explain_architecture` | Route architecture questions to the best expert |
| `create_pr` | Return repository-specific pull request guidance |
| `list_agents` | Discover available expert agents and skills |
| `ask_agent` | Send a question to a specific expert agent |
| `list_repositories` | List managed repositories from the registry or seed data |
| `ask_repo_expert` | Ask the expert that owns a repository |
| `submit_followup` | Continue an existing MCP conversation thread |

Initialize via JSON-RPC 2.0 on `/mcp`, call `tools/list`, then invoke tools with `tools/call`.

## VS Code Extension

The extension integrates expert agents as Copilot Chat participants:

- **@fhir-server** — Route questions to the FHIR Server Expert
- **@healthcare-components** — Route questions to the Healthcare Components Expert

Features:
- Streaming responses with markdown rendering
- Follow-up question suggestions
- Works offline with mock responses when backend is unavailable
- Configurable backend URL via settings

## Connecting Claude Code via MCP

Claude Code can connect to Expert Agents in two ways:

1. **HTTP transport**
   ```bash
   claude mcp add expert-agents \
     --transport http \
     --url http://localhost:5000/mcp
   ```
2. **stdio bridge transport**
   ```bash
   claude mcp add expert-agents \
     --transport stdio \
     --command "experts-mcp" \
     --args "--url" \
     --args "http://localhost:5000/mcp"
   ```

Available tools: `search_code`, `get_file_content`, `explain_architecture`, `create_pr`, `list_agents`, `ask_agent`, `list_repositories`, `ask_repo_expert`, `submit_followup`.

Example `search_code` payload:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "search_code",
    "arguments": {
      "repo": "fhir-server",
      "query": "export"
    }
  }
}
```

Example `list_agents` payload:
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "list_agents",
    "arguments": {}
  }
}
```

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

## LLM Configuration

Expert Agents uses `Microsoft.Extensions.AI` for provider-agnostic LLM access. Configure via `appsettings.json` (`Llm` section) or user secrets.

### Default (Mock)
No configuration needed. The system uses a built-in mock LLM for development/testing.

### Azure OpenAI
```bash
dotnet user-secrets set "Llm:DefaultProvider" "AzureOpenAI" --project src/AgentHost
dotnet user-secrets set "Llm:Providers:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/" --project src/AgentHost
dotnet user-secrets set "Llm:Providers:AzureOpenAI:ApiKey" "<your-key>" --project src/AgentHost
```

### OpenAI
```bash
dotnet user-secrets set "Llm:DefaultProvider" "OpenAI" --project src/AgentHost
dotnet user-secrets set "Llm:Providers:OpenAI:ApiKey" "sk-..." --project src/AgentHost
```

### Anthropic
```bash
dotnet user-secrets set "Llm:DefaultProvider" "Anthropic" --project src/AgentHost
dotnet user-secrets set "Llm:Providers:Anthropic:ApiKey" "sk-ant-..." --project src/AgentHost
```

### Ollama (local)
```bash
dotnet user-secrets set "Llm:DefaultProvider" "Ollama" --project src/AgentHost
dotnet user-secrets set "Llm:Providers:Ollama:Endpoint" "http://localhost:11434" --project src/AgentHost
```

### Per-agent overrides
Add to `Llm:AgentOverrides` to route a specific agent to a different provider:
```json
"AgentOverrides": {
  "fhir-server-expert": { "Provider": "Anthropic", "Model": "claude-sonnet-4-5" }
}
```
