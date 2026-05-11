# Expert Agents System — Architecture Specification

## Overview
Multi-agent development platform where "expert" agents own specific code repositories. Agents support A2A protocol for inter-agent communication, MCP for tool integration, code indexing/RAG for repository understanding, and can create PRs on behalf of users.

## Components

### 1. AgentHost (ASP.NET Core + .NET Aspire)
- Hosts multiple expert agents as A2A-compliant servers
- Exposes A2A endpoints: `POST /tasks/send`, `POST /tasks/sendSubscribe`, `GET /.well-known/agent-card.json`
- MCP server integration for tool discovery
- Agent discovery and routing service
- SignalR hub for real-time web chat communication

### 2. Expert Agents (2 demo agents)
**FHIRServerAgent**: Owns microsoft/fhir-server repository knowledge
- Skills: code search, architecture Q&A, PR creation guidance, FHIR spec knowledge
- Agent Card: `/.well-known/agent-card.json`

**HealthcareSharedComponentsAgent**: Owns microsoft/healthcare-shared-components
- Skills: component library docs, dependency analysis, code examples, migration guidance
- Agent Card: `/.well-known/agent-card.json`

### 3. CodeIndexService
- Indexes repository code using Qdrant vector DB
- AST-based chunking via TreeSitter.DotNet
- voyage-code-3 embeddings (or OpenAI fallback)
- Hybrid search API for agents
- Incremental indexing support

### 4. WebChat (React + TypeScript)
- Chat interface for talking to expert agents
- @-mention routing to specific agents
- SSE streaming for real-time responses
- Markdown rendering with syntax highlighting
- Agent status indicators

### 5. VSCodeExtension (TypeScript)
- Copilot Chat participant: `@fhir-expert` and `@healthcare-expert`
- Integrates with AgentHost via HTTP/A2A
- Streams responses to Copilot Chat UI

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 9, ASP.NET Core, SignalR |
| Agent Framework | Microsoft Agent Framework (Semantic Kernel) |
| Protocols | A2A (JSON-RPC 2.0 over HTTP), MCP (stdio + HTTP) |
| Vector DB | Qdrant (Docker) |
| Embeddings | voyage-code-3 or OpenAI text-embedding-3-small |
| Frontend | React 19, TypeScript, Tailwind CSS, Assistant-UI |
| VS Code Ext | TypeScript, VS Code Extension API |
| Hosting | .NET Aspire (local dev), container-ready |

## A2A Protocol Implementation

### Agent Card Schema
```json
{
  "name": "fhir-server-expert",
  "description": "Expert agent for Microsoft FHIR Server - answers architecture questions, searches code, guides PR creation",
  "version": "1.0.0",
  "url": "http://localhost:5001",
  "capabilities": {
    "streaming": true,
    "pushNotifications": false
  },
  "skills": [
    {
      "name": "code-search",
      "description": "Search the FHIR Server codebase using semantic search",
      "inputModes": ["text"],
      "outputModes": ["text", "file"]
    },
    {
      "name": "architecture-qa",
      "description": "Answer deep technical questions about FHIR Server architecture",
      "inputModes": ["text"],
      "outputModes": ["text"]
    },
    {
      "name": "pr-guidance",
      "description": "Guide users through creating PRs for the FHIR Server repo",
      "inputModes": ["text"],
      "outputModes": ["text"]
    }
  ]
}
```

### Task Lifecycle API
```
POST /tasks/send           - Send task (sync)
POST /tasks/sendSubscribe  - Send task with SSE streaming
GET  /tasks/{taskId}       - Get task status
POST /tasks/{taskId}/cancel - Cancel task
GET  /.well-known/agent-card.json - Agent discovery
```

## MCP Server Design

### Tools Exposed
| Tool | Description |
|------|-------------|
| `search_code` | Semantic search across indexed repositories |
| `get_file_content` | Retrieve specific file content |
| `explain_architecture` | Get architecture overview of a component |
| `create_pr` | Create a PR on a repository (guidance + link) |

## Project Structure

```
/mnt/agents/output/expert-agents-prototype/
├── src/
│   ├── AgentHost/                    # ASP.NET Core host
│   │   ├── AgentHost.csproj
│   │   ├── Program.cs
│   │   ├── Agents/                   # Expert agent implementations
│   │   │   ├── FhirServerAgent.cs
│   │   │   └── HealthcareSharedComponentsAgent.cs
│   │   ├── A2A/                      # A2A protocol layer
│   │   │   ├── A2AController.cs
│   │   │   ├── AgentCardProvider.cs
│   │   │   └── TaskManager.cs
│   │   ├── MCP/                      # MCP server layer
│   │   │   └── CodeMcpServer.cs
│   │   ├── Services/                 # Shared services
│   │   │   ├── CodeIndexService.cs
│   │   │   └── GitHubPrService.cs
│   │   └── Hubs/
│   │       └── ChatHub.cs            # SignalR hub
│   ├── CodeIndexService/             # Code indexing worker
│   │   ├── CodeIndexService.csproj
│   │   ├── Program.cs
│   │   ├── Chunkers/
│   │   │   └── TreeSitterChunker.cs
│   │   └── Embedders/
│   │       └── VoyageCodeEmbedder.cs
│   └── ExpertAgents.AppHost/         # .NET Aspire orchestrator
│       └── Program.cs
├── web-chat/                         # React chat interface
│   ├── package.json
│   ├── src/
│   │   ├── App.tsx
│   │   ├── components/
│   │   │   ├── ChatInterface.tsx
│   │   │   ├── MessageList.tsx
│   │   │   └── AgentStatus.tsx
│   │   └── services/
│   │       └── agentClient.ts
│   └── index.html
├── vscode-extension/                 # VS Code extension
│   ├── package.json
│   ├── src/
│   │   └── extension.ts
│   └── tsconfig.json
├── ExpertAgents.sln
└── README.md
```

## API Contracts

### Agent Chat Request
```json
POST /api/chat
{
  "agentId": "fhir-server-expert",
  "message": "How does the FHIR server handle custom search parameters?",
  "sessionId": "uuid",
  "stream": true
}
```

### Code Search Request
```json
POST /api/code/search
{
  "repo": "microsoft/fhir-server",
  "query": "custom search parameters implementation",
  "topK": 5
}
```

### Agent-to-Agent Delegation
```json
POST /a2a/tasks/send
{
  "id": "task-123",
  "message": {
    "role": "user",
    "parts": [{"type": "text", "text": "What shared components does the FHIR server depend on?"}]
  }
}
```

## Implementation Order
1. AgentHost + A2A protocol layer
2. Expert agent definitions + mock responses
3. CodeIndexService (simplified)
4. Web Chat interface
5. VS Code extension
6. Integration + testing
