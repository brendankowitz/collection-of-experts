## Facet: MCP (Model Context Protocol) Integration Patterns

### Key Findings

- **MCP is an open standard introduced by Anthropic in November 2024** for connecting AI applications to external tools, data sources, and services. It is metaphorically described as "USB-C for AI" — one protocol that works everywhere. [^55^] [^46^]

- **The architecture consists of four core components**: Hosts (AI apps like Claude Desktop, VS Code, Cursor), Clients (one per server, managing connections), Servers (processes exposing tools/resources/prompts), and the Protocol itself (JSON-RPC 2.0 over stdio or HTTP). [^55^] [^54^]

- **Three primitives define server capabilities**: Tools (executable actions), Resources (read-only data entities), and Prompts (reusable templates for LLM interactions). [^100^] [^102^] [^103^]

- **The session lifecycle is a strict three-phase process**: Initialization (capability negotiation handshake), Operation (bidirectional message exchange), and Shutdown (connection termination). The initialization handshake is mandatory — no requests except `initialize` are permitted until it completes. [^97^] [^54^]

- **SSE transport was officially deprecated in March 2025** and replaced by Streamable HTTP, which uses standard HTTP POST + optional SSE streaming for responses. Streamable HTTP supports both stateless (serverless-friendly) and stateful modes via `Mcp-Session-Id` headers. [^218^] [^40^]

- **Claude Code integrates MCP via `claude mcp add` CLI commands** or `.mcp.json` / `settings.json` configuration files, supporting stdio, SSE, HTTP, and WebSocket transports. Claude Code manages stdio server processes as child processes and handles OAuth flows automatically for remote servers. [^41^] [^42^] [^45^]

- **The tool discovery flow follows a standardized pattern**: `initialize` handshake → `tools/list` discovery → `tools/call` invocation. Servers can dynamically notify clients of tool changes via `notifications/tools/list_changed`. [^241^] [^243^] [^240^]

- **Authentication patterns include API keys, OAuth 2.1, and mTLS**. The MCP specification explicitly references OAuth 2.1 (RFC 9700), mandates token audience validation, prohibits session-based authentication, and requires per-client consent. [^52^] [^50^]

- **Major security vulnerabilities have been discovered**: CVE-2025-53109 (symlink bypass to code execution, CVSS 8.4) and CVE-2025-53110 (directory containment bypass, CVSS 7.3) in Anthropic's Filesystem MCP Server. [^221^]

- **Microsoft has fully embraced MCP** with official .NET SDK (`Microsoft.McpServer.ProjectTemplates`), TypeScript SDK (`@modelcontextprotocol/sdk`), VS Code Copilot integration, .NET Aspire MCP server support, and an official MS Learn MCP server. [^53^] [^229^] [^244^] [^250^]

### Major Players & Sources

- **Anthropic**: Protocol creator and specification maintainer. Operates `modelcontextprotocol.io` as the official docs hub. Publishes official SDKs for Python and TypeScript, and reference servers (filesystem, GitHub, Slack). [^248^]
- **Microsoft**: Major ecosystem adopter. Provides .NET SDK (`Microsoft.McpServer.ProjectTemplates`), TypeScript SDK, VS Code + GitHub Copilot MCP integration, .NET Aspire MCP support, Dataverse MCP server, and MS Learn MCP server. [^53^] [^229^] [^237^] [^250^]
- **GitHub**: Official GitHub MCP server for repository management, issues, PRs. Also provides MCP Registry in VS Code. [^43^] [^237^]
- **Red Hat**: Building MCP Gateway on Kubernetes (Kuadrant project) for enterprise traffic management, auth, and rate limiting. [^232^]
- **Stripe**: Official Stripe MCP server for billing/transaction management. [^43^]
- **n8n**: Workflow automation platform with visual MCP server builder. [^43^]
- **Cursor, Windsurf, OpenCode**: AI coding tools that function as MCP hosts. [^42^]
- **Box**: Enterprise MCP server with dual authentication (MCP client auth + Box API auth). [^58^]

### Trends & Signals

- **Streamable HTTP replacing SSE**: The March 2025 specification revision deprecated SSE in favor of Streamable HTTP, which works better with serverless platforms, load balancers, and CDNs. All major SDKs now support Streamable HTTP natively. [^218^]

- **Gateway pattern emerging as enterprise consensus**: Organizations running 3+ MCP servers are moving toward centralized gateways for auth, dynamic tool loading, output compression, and monitoring. Red Hat, Apigene, and others are building gateway solutions. [^232^] [^219^]

- **Dynamic tool discovery to reduce token bloat**: Teams are implementing lazy tool loading — exposing only relevant tools per session rather than all tools from all servers. This can reduce tool definition overhead by up to 70%. [^242^] [^219^]

- **Microsoft ecosystem-wide adoption**: MCP is being integrated across VS Code, GitHub Copilot, .NET Aspire, Power Apps, and Visual Studio, making it a de facto standard in the Microsoft developer ecosystem. [^229^] [^231^] [^250^]

- **Security maturation**: Published CVEs, formal security requirements (OAuth 2.1, audience validation, session prohibition), and enterprise auth patterns (mTLS, per-tool RBAC) indicate the ecosystem is maturing beyond prototyping. [^52^] [^221^] [^50^]

- **Client capability gap limiting protocol advancement**: Many clients only support basic features (tools, resources), creating a "lowest common denominator" problem that discourages servers from implementing advanced features like sampling, elicitation, and resource subscriptions. [^245^]

### Controversies & Conflicting Claims

- **Security vs. convenience tension**: The filesystem server CVEs (2025-53109, 2025-53110) exposed fundamental sandboxing flaws. Directory prefix matching and symlink handling proved inadequate, allowing attackers to escape allowed directories and achieve code execution. Anthropic patched these in version 2025.7.1. [^221^]

- **Session-based auth prohibited by spec**: The MCP specification explicitly prohibits session-based authentication, requiring token validation on every request. This eliminates session hijacking vulnerabilities but creates architectural challenges for existing applications relying on session cookies. [^52^]

- **SSE deprecation migration friction**: While Streamable HTTP is the recommended replacement, older clients still rely on SSE. Bridge tools like MCPO and Supergateway exist but are described as "fragile" for production use. [^218^]

- **"Lowest common denominator" stagnation**: The client capability gap means many servers can't leverage advanced protocol features because popular clients don't support them, potentially slowing ecosystem evolution. [^245^]

- **Defense in depth debates**: Some advocate for layered auth (mTLS + OAuth + API keys) while others argue this adds operational complexity. The consensus is to match auth to threat model: API keys for internal tools, OAuth for multi-tenant, mTLS for regulated environments. [^50^] [^58^]

### Recommended Deep-Dive Areas

- **MCP Gateway Architecture**: How Red Hat, Apigene, and others are building centralized MCP management layers for enterprise scale. This is where production MCP deployments are heading. [^232^] [^219^]

- **Dynamic Tool Discovery & Token Optimization**: The `notifications/tools/list_changed` mechanism and lazy loading patterns that reduce context window bloat. Critical for scaling past 3-5 MCP servers. [^242^] [^240^]

- **MCP Security Deep Dive**: OAuth 2.1 implementation, CVE analysis, per-tool RBAC, and the "authentication proxy pattern" for multi-backend deployments. [^52^] [^50^] [^58^]

- **Microsoft MCP Ecosystem Integration**: How .NET Aspire, VS Code Copilot, and the .NET SDK are converging to make MCP a first-class citizen in the Microsoft stack. [^244^] [^247^] [^250^]

- **Sampling and Elicitation Capabilities**: Server-to-client LLM completion requests and interactive user input collection. These enable advanced agentic workflows but adoption is limited by client support. [^105^] [^54^]

---

### Detailed Notes

#### 1. MCP Architecture Overview

MCP follows a layered client-server architecture [^55^] [^54^]:

**Host**: The AI application that coordinates clients and uses provided context. Examples: Claude Desktop, VS Code with Copilot, Cursor, ChatGPT. The host manages multiple MCP clients — one per connected server.

**Client**: Instantiated by the host — one per server. Handles the dedicated connection, capability discovery, and primitive invocation. Speaks JSON-RPC 2.0 over stdio or HTTP transport. Each client maintains its own message loop, capability negotiation, authentication, and tool routing. [^96^]

**Server**: Exposes context to clients. Can be local (e.g., filesystem server on same machine) or remote (hosted service over HTTPS). Declares Tools, Resources, and Prompts on connect. [^107^]

**Protocol**: JSON-RPC 2.0 encoded messages, UTF-8 required. Messages are newline-delimited and MUST NOT contain embedded newlines. [^40^]

The three primitives servers expose [^102^] [^103^]:
- **Tools**: Executable functions that perform actions or computations. The LLM decides when to call them. Registered via `registerTool()` in TypeScript or `@mcp.tool()` in Python.
- **Resources**: Data entities (static or dynamic) identified by URIs. Clients read them via `resources/read`. Can support subscriptions for change notifications.
- **Prompts**: Reusable templates that help structure LLM interactions. Users invoke them explicitly. Registered via `registerPrompt()`.

**Session Lifecycle** [^97^] [^108^]:
1. **Initialization**: Client sends `initialize` request with `protocolVersion`, `capabilities`, `clientInfo`. Server responds with its capabilities and `serverInfo`. Client sends `notifications/initialized` notification.
2. **Operation**: Bidirectional message exchange. Client calls `tools/list`, `tools/call`, `resources/list`, `prompts/list`, etc. Server can send notifications.
3. **Shutdown**: Graceful close via transport termination. No specific protocol messages required.

**Capability Negotiation** [^97^] [^98^]:
- Client capabilities: `roots`, `sampling`, `elicitation`
- Server capabilities: `tools`, `resources`, `prompts`, `logging`, `completions`
- Sub-capabilities: `listChanged`, `subscribe`
- Both sides must only use features explicitly agreed upon during handshake

#### 2. Transports: stdio vs. Streamable HTTP

**stdio Transport** [^40^] [^44^]:
- Client launches MCP server as a subprocess
- Server reads JSON-RPC from stdin, writes to stdout
- stderr reserved for logging (MUST NOT contain non-MCP data in stdout)
- Single client per server process
- Best for: CLI tools, local integrations, desktop apps
- Zero infrastructure, zero network config, perfect isolation

**Streamable HTTP Transport** [^40^] [^218^]:
- Replaced SSE as the recommended remote transport (March 2025 spec revision)
- Client POSTs JSON-RPC messages to a single endpoint (typically `/mcp`)
- Server responds with single JSON or opens SSE stream for multiple messages
- Supports `Mcp-Session-Id` header for optional stateful sessions
- Works with serverless platforms (Cloud Run, Lambda, Azure Functions)
- Compatible with load balancers, proxies, CDNs
- Can operate stateless (each request independent) or stateful (session persists)
- Best for: production remote servers, web clients, multi-user deployments

**Comparison** [^44^] [^218^]:

| Feature | stdio | SSE (deprecated) | Streamable HTTP |
|---------|-------|-----------------|-----------------|
| Deployment | Local process | HTTP server | HTTP server |
| Remote support | No | Yes | Yes |
| Serverless compatible | No | No | Yes |
| Session management | Implicit | Required | Optional |
| Load balancer friendly | N/A | No | Yes |
| Multi-user routing | No | Difficult | Native |

**Key transport rules** [^40^]:
- Servers MAY write UTF-8 strings to stderr for logging
- Servers MUST NOT write anything to stdout that isn't a valid MCP message (stdio)
- Clients SHOULD support stdio whenever possible
- Custom transports can be implemented in a pluggable fashion

#### 3. Claude Code MCP Integration Patterns

Claude Code integrates MCP servers through multiple mechanisms [^42^] [^41^]:

**Configuration locations**:
- User scope: `~/.claude/settings.json` (available to all sessions)
- Project scope: `.claude/settings.json` (project-specific, overrides user scope)

**CLI commands**:
- `claude mcp add <name> <command> [args...]` — register a server
- `claude mcp list` — show registered servers
- `claude mcp test <name>` — verify a server
- `claude mcp remove <name>` — unregister

**Server types supported** [^41^]:

| Type | Configuration | Use Case |
|------|--------------|----------|
| stdio | `command`, `args`, `env` | Local tools, custom servers |
| SSE | `type: "sse"`, `url` | Hosted servers, OAuth services |
| HTTP | `type: "http"`, `url`, `headers` | REST API backends |
| WebSocket | `type: "ws"`, `url` | Real-time streaming |

**stdio example**:
```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_..."
      }
    }
  }
}
```

**Process management**: Claude Code spawns stdio servers as child processes, communicates via stdin/stdout, and terminates them when Claude Code exits. [^41^]

**Tool naming convention**: Tools follow the pattern `mcp__<server-name>__<tool-name>`. [^217^]

#### 4. Popular MCP Servers for Development

Top MCP servers ranked by utility and adoption [^43^] [^99^] [^104^]:

| Server | Category | Key Tools |
|--------|----------|-----------|
| `github/github-mcp-server` | Dev/SCM | Read repos, search, manage PRs, create branches |
| `@modelcontextprotocol/server-filesystem` | Dev/File | Read/write files within allowed directories |
| `microsoft/playwright-mcp` | Dev/Test | Browser automation, E2E testing |
| `stripe/stripe-mcp` | Business | Query subscriptions, check transactions |
| `postmanlabs/postman-mcp-server` | Dev/API | Run API collections, test endpoints |
| `atlassian/atlassian-mcp-server` | Business | Jira tickets, issue transitions |
| `makenotion/notion-mcp-server` | Business | Read/write wikis, PRD access |
| `upstash/context7` | Dev/Docs | Technical documentation search |
| `slack` | Business | Send messages, channel management |
| `grafana/mcp-grafana` | Infra/Obs | Query metrics, retrieve dashboards |
| `pagerduty/pagerduty-mcp-server` | Infra/Obs | Incident management, on-call |
| `sentry` | Infra/Obs | Error tracking, stack trace analysis |

**Community registries**: `awesome-mcp-servers` tracks 1,076+ servers across 39 categories. [^104^]

#### 5. Building Custom MCP Servers

**TypeScript SDK** (`@modelcontextprotocol/sdk`) [^100^] [^103^]:
```typescript
import { McpServer } from '@modelcontextprotocol/server';
import { StdioServerTransport } from '@modelcontextprotocol/server/stdio';

const server = new McpServer(
  { name: 'my-server', version: '1.0.0' },
  { capabilities: { tools: {} } }
);

server.registerTool('echo', {
  description: 'Echo a message back',
  inputSchema: z.object({ message: z.string() })
}, async ({ message }) => ({
  content: [{ type: 'text', text: `Echo: ${message}` }]
}));

const transport = new StdioServerTransport();
await server.connect(transport);
```

**Required dependencies**: `npm install @modelcontextprotocol/sdk zod`
- Supports Streamable HTTP, stdio, and HTTP+SSE (deprecated)
- Full support for tools, resources, prompts, sampling, elicitation, tasks
- Peer dependency on `zod` for schema validation

**.NET SDK** (`Microsoft.McpServer.ProjectTemplates`) [^53^]:
```bash
dotnet new install Microsoft.McpServer.ProjectTemplates
dotnet new mcpserver -n SampleMcpServer
```
- Requires .NET 10.0 SDK or later
- Project templates support both local (stdio) and remote (HTTP) transports
- Visual Studio 2022 and VS Code with C# Dev Kit supported
- Integrates with GitHub Copilot for testing

**Python (FastMCP)** [^216^]:
```python
from fastmcp import FastMCP
from app.main import server

mcp = FastMCP.from_fastapi(app=server)
mcp.run()
```
- FastMCP converts FastAPI routes to MCP tools automatically
- Black-box approach — fast setup but limited visibility
- Preserves Pydantic validations, dependencies, middleware

**MCP SDK .NET NuGet package** (`ModelContextProtocol`) [^247^]:
- Can be used directly for building custom MCP clients and servers
- Example: `ModelContextProtocol@0.4.1-preview.1` NuGet package
- Supports stdio and HTTP transports

#### 6. Tool Discovery and Invocation Flow

The complete tool flow [^241^] [^243^] [^55^]:

**Step 1: Initialization**
```json
// Client sends
{ "method": "initialize",
  "params": {
    "protocolVersion": "2025-06-18",
    "capabilities": { "sampling": {} },
    "clientInfo": { "name": "MyHost", "version": "1.0.0" }
  }
}

// Server responds
{ "result": {
    "protocolVersion": "2025-06-18",
    "capabilities": { "tools": { "listChanged": true } },
    "serverInfo": { "name": "my-server", "version": "1.0.0" }
}}

// Client confirms
{ "method": "notifications/initialized" }
```

**Step 2: Discovery (`tools/list`)**
```json
// Client sends
{ "jsonrpc": "2.0", "id": 1, "method": "tools/list" }

// Server responds
{ "tools": [
    {
      "name": "get_weather",
      "description": "Get current weather for a location",
      "inputSchema": {
        "type": "object",
        "properties": {
          "location": { "type": "string", "description": "City name" }
        },
        "required": ["location"]
      }
    }
  ]
}
```

**Step 3: Invocation (`tools/call`)**
```json
// Client sends
{ "jsonrpc": "2.0", "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_weather",
    "arguments": { "location": "San Francisco" }
  }
}

// Server responds
{ "content": [
    { "type": "text", "text": "Current weather in San Francisco: 72°F, sunny" }
  ]
}
```

**Dynamic updates**: If the server declared `listChanged: true`, it sends `notifications/tools/list_changed` whenever tools are added, removed, or modified. The client should re-call `tools/list` on receipt. [^240^] [^243^]

**Tool annotations**: Tools can include metadata hints like `readOnlyHint`, `destructiveHint`, `idempotentHint`, and `openWorldHint` to help clients understand tool behavior. [^249^]

#### 7. Authentication and Security

**Three dominant auth patterns** [^50^] [^52^]:

| Factor | API Keys | OAuth 2.1 | Mutual TLS |
|--------|----------|-----------|------------|
| Implementation effort | Low | Medium | High |
| Credential rotation | Manual | Automatic | Certificate renewal |
| Revocation speed | Immediate | Up to token TTL | Immediate (CRL/OCSP) |
| Multi-tenant support | With scopes | Native | Per-tenant certs |
| Best for | Internal/dev tooling | Multi-tenant, user-delegated | Zero-trust infra |

**MCP specification security requirements** [^52^]:
- OAuth 2.1 is the foundation (Authorization Code with PKCE, Client Credentials)
- Token audience validation is mandatory — verify `aud` claim on every request
- Session-based authentication is **prohibited** — all auth must use tokens validated per-request
- Five mandatory authorization patterns: per-client consent, token audience validation, rejection of token passthrough, exact redirect URI matching, OAuth state parameter validation
- Short-lived tokens (15-60 minutes recommended) with refresh token rotation

**Layered production pattern** [^50^]:
- mTLS at transport layer (authorized clients only)
- OAuth tokens in headers (fine-grained scope per tool call)
- API keys as fallback for internal health checks

**CVEs discovered** [^221^]:
- **CVE-2025-53109** (CVSS 8.4): Symlink bypass to code execution in Filesystem MCP Server. Crafted symlinks could point anywhere on the filesystem, bypassing access controls.
- **CVE-2025-53110** (CVSS 7.3): Directory containment bypass via naive prefix matching (`startsWith` check). Paths like `/allowed_dir_sensitive` would bypass filtering.
- Patched in version `2025.7.1`. Affected all versions prior to `0.6.3` and `2025.7.1`.

**Client-side security** [^96^]:
- CVE-2025-6514 in mcp-remote: malicious server injected shell commands through crafted `authorization_endpoint` URL
- CVE-2025-49596 in MCP Inspector: remote code execution through unsanitized client-side handling
- Clients must sanitize server-supplied data, validate URLs, and store tokens securely

#### 8. Best Practices for MCP Server Implementation

**Tool descriptions** [^219^]:
- Start with the verb: "Creates a new Jira ticket with the specified fields"
- Include parameter constraints: "Accepts a SQL SELECT query (read-only, max 1000 rows)"
- Specify return format: "Returns a JSON array of {id, name, email}"
- Add negative instructions: "Do NOT use for bulk operations over 100 records"
- One developer reported rewriting descriptions reduced misrouted calls from 23% to under 5%

**Logging** [^48^] [^235^]:
- For stdio: NEVER use `Console.WriteLine()` or `Console.Out.Write()` — stdout is reserved for JSON-RPC
- Use stderr for logging in stdio mode: `Console.Error.WriteLine()` or a logging library
- For HTTP transports: standard output logging is fine
- Use RFC 5424 severity levels (debug through emergency)

**Error handling** [^219^] [^217^]:
- Return structured error responses with `isError: True` rather than throwing uncaught exceptions
- Include `error_type`, `message`, `retry_after_seconds`, and `suggestions` in error responses
- This allows the agent to decide whether to retry, use cached data, or ask the user

**Dynamic tool loading** [^219^] [^242^]:
- Expose only relevant tools per session based on agent role and context
- Reduces tool definition overhead by up to 70%
- Use `notifications/tools/list_changed` to dynamically update available tools

**Credential management** [^219^] [^51^]:
- Never store credentials in MCP config files
- Use environment variables with per-project scoping
- Use a secrets manager (AWS SSM, Doppler, HashiCorp Vault)
- Route through a gateway that holds credentials centrally

**Transport best practices** [^44^]:
- Default to stdio for CLI and desktop integrations
- Use Streamable HTTP for production remote servers
- Create a new server instance per connection for HTTP transports
- Implement health check endpoints for HTTP transports
- Set appropriate timeouts (SSE: 30s keep-alive, WebSocket: 30s ping/pong)

**Versioning and deployment** [^219^]:
- Pin to specific server versions in production
- Maintain backwards compatibility for at least one version
- Document breaking changes
- Separate dev and production environments
- Use Streamable HTTP for production transport

**MCP Inspector for debugging** [^233^] [^235^]:
```bash
npx @modelcontextprotocol/inspector
npx @modelcontextprotocol/inspector npx -y @modelcontextprotocol/server-filesystem /tmp
```
- Interactive browser-based tool for testing MCP servers
- Supports stdio, SSE, and Streamable HTTP transports
- Lists tools, resources, prompts; allows invocation with custom parameters
- Also available as VS Code extension

#### 9. Client-Side MCP Integration

**VS Code + GitHub Copilot** [^229^] [^237^]:
- MCP servers configured via `.vscode/mcp.json` (workspace) or user profile
- Servers discovered via `@mcp` search in Extensions view
- Supports toolsets (groups of related tools)
- Can reuse Claude Desktop configurations via `chat.mcp.discovery.enabled`
- Supports Dev Container configuration via `devcontainer.json`

**.NET Aspire MCP** [^246^] [^247^]:
- `aspire mcp init` (being renamed to `aspire agent mcp`) for automatic setup
- Two transport modes: stdio (CLI approach, recommended) and HTTP (dashboard approach)
- Provides tools for querying resources, accessing logs, investigating telemetry, executing commands
- MCP proxy bridges stdio client to HTTP Aspire server

**Microsoft Learn MCP Server** [^250^]:
- Managed content provider for Copilot with Microsoft product documentation
- Available for Visual Studio, VS Code, Copilot CLI, and Copilot Coding Agent
- Provides up-to-date, context-aware docs, code samples, and learning resources

#### 10. Protocol Version and Evolution

- **Current protocol version**: Date-string versioned (e.g., `2025-06-18`). [^55^]
- **Version negotiation**: Client proposes version during `initialize`; server responds with compatible version. If versions are incompatible, disconnect. [^97^]
- **Schema**: Defined in TypeScript-first at `schema.ts`, also exported as JSON. [^55^]
- **Capability sub-capabilities**: `listChanged`, `subscribe` for precise agreement. [^97^]
- **Experimental features**: Tasks (long-running tool calls with polling/resumption). [^103^]
- **Notification types**: `notifications/initialized`, `notifications/tools/list_changed`, `notifications/resources/updated`, `notifications/resources/list_changed`. [^243^]