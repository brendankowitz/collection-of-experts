# Multi-Agent Development "Expert Agents" — Research & Prototype Plan

## Overview
Research and build a prototype for multi-agent development system where "expert" agents own specific code repositories (e.g., microsoft/fhir-server, microsoft/healthcare-shared-components). These agents should be capable of code indexing, PR creation, deep technical Q&A, and inter-agent communication via A2A/MCP protocols. The solution should be Microsoft-stack friendly and demonstrate integration with Claude Code, VS Code Copilot Chat, and a web chat interface.

## Stage 1 — Deep Research (deep-research-swarm)
**Objective**: Comprehensive research across multiple dimensions in parallel.

### Research Tracks (parallel agents):
1. **A2A Protocol & MCP Research**: Google's Agent-to-Agent (A2A) protocol, Anthropic's Model Context Protocol (MCP), how they enable agent communication, discovery, and tool calling. Current implementations and SDKs.
2. **OpenClaw / ClawPilot / SRE Agents**: Existing "claw" ecosystem tools — OpenClaw (open source alternatives), ClawPilot (GitHub/Microsoft integration), SRE agent patterns. What exists, what's feasible.
3. **Microsoft Health Repos Architecture**: Deep dive into microsoft/fhir-server and microsoft/healthcare-shared-components — their structure, extension points, how an agent could index and reason about them.
4. **Agent Hosting & Orchestration**: How to host multiple agents — Azure Container Apps, Azure Functions, ASP.NET Core hosted services, Orleans, DAPR. Microsoft-stack options.
5. **VS Code Extension & Copilot Chat Integration**: VS Code extension API, Copilot Chat API participation, how to integrate external agents into the IDE experience.
6. **Code Indexing & RAG for Repos**: Techniques for indexing large codebases — tree-sitter, semantic search, vector DBs (Azure AI Search, Qdrant), GraphRAG for code relationships.

**Output**: Validated research brief with findings, architecture patterns, and technical recommendations.

## Stage 2 — Architecture Design (report-writing)
**Objective**: Design the system architecture based on research findings.

### Deliverables:
1. **Architecture Document**: Full system design with component diagrams
2. **Protocol Design**: How agents communicate via A2A/MCP
3. **Agent Capabilities Matrix**: What each expert agent can do
4. **Integration Points**: Claude Code, VS Code, Web Chat, Teams
5. **Deployment Model**: How agents are hosted and discovered

**Output**: Architecture specification document (markdown).

## Stage 3 — Prototype Implementation (vibecoding-general-swarm)
**Objective**: Build a working prototype with the following components:

### Components:
1. **Agent Host Service** (.NET Aspire / ASP.NET Core): Hosts multiple expert agents, exposes A2A/MCP endpoints
2. **FHIR Server Expert Agent**: Owns microsoft/fhir-server knowledge, can answer questions
3. **Healthcare Shared Components Expert Agent**: Owns microsoft/healthcare-shared-components
4. **Agent Coordinator/Orchestrator**: Routes requests to appropriate agents, handles inter-agent communication
5. **Web Chat Interface**: React-based chat UI to talk to agents
6. **VS Code Extension Sample**: Basic extension showing Copilot Chat integration pattern
7. **A2A Discovery Service**: Agents can discover and communicate with each other
8. **Claude Code Integration Bridge**: MCP server that Claude Code can connect to

### Tech Stack:
- **Backend**: .NET 9, ASP.NET Core, SignalR (real-time chat), Azure AI Search (vector DB), Semantic Kernel
- **Frontend**: React + TypeScript chat interface
- **Protocols**: A2A (JSON-RPC over HTTP/SSE), MCP (Server-Sent Events)
- **Hosting**: .NET Aspire for local dev, container-ready
- **VS Code Extension**: TypeScript, VS Code Extension API

## Stage 4 — Documentation & Delivery
**Objective**: Package everything with clear documentation.

### Deliverables:
1. README with setup instructions
2. Architecture documentation
3. Demo scripts
4. Deployable artifacts

**Output**: Complete prototype package in `/mnt/agents/output/expert-agents-prototype/`.
