# MCP Server Implementation (.NET & TypeScript) - Research Findings

## Table of Contents
1. [Executive Summary](#1-executive-summary)
2. [Technical Architecture & Components](#2-technical-architecture--components)
3. [.NET Implementation Details](#3-net-implementation-details)
4. [TypeScript Implementation Details](#4-typescript-implementation-details)
5. [Transport Configuration](#5-transport-configuration)
6. [Tool Discovery & Invocation Protocol](#6-tool-discovery--invocation-protocol)
7. [Authentication Patterns](#7-authentication-patterns)
8. [Packaging & Distribution](#8-packaging--distribution)
9. [Error Handling & Logging](#9-error-handling--logging)
10. [VS Code Extension Integration](#10-vs-code-extension-integration)
11. [Limitations & Gotchas](#11-limitations--gotchas)
12. [Recommendations for Prototype](#12-recommendations-for-prototype)
13. [Sources & References](#13-sources--references)

---

## 1. Executive Summary

The Model Context Protocol (MCP) is an open standard (originally by Anthropic, now maintained by the Agentic AI Foundation) that gives AI models a universal way to discover, understand, and use external tools and data sources. It uses JSON-RPC 2.0 for message encoding and supports two primary transports: **stdio** (local, single-client) and **Streamable HTTP** (remote, multi-client) [^40^][^377^].

**Key Findings:**
- **.NET**: Microsoft provides official C# SDK via three NuGet packages: `ModelContextProtocol`, `ModelContextProtocol.Core`, and `ModelContextProtocol.AspNetCore` (latest: v1.2.0). The `Microsoft.McpServer.ProjectTemplates` package provides project scaffolding [^410^][^411^][^413^].
- **TypeScript**: The official `@modelcontextprotocol/sdk` is the Tier-1 SDK with 66M+ npm downloads. Uses Zod for schema validation and supports both stdio and Streamable HTTP transports [^355^][^412^].
- **Tool Discovery**: Automatic via `tools/list` JSON-RPC method. Tools are decorated with `[McpServerTool]` in .NET or registered via `server.registerTool()` in TypeScript [^377^][^400^].
- **Authentication**: Bearer token for internal tools; OAuth 2.1 + PKCE required for public remote servers per the November 2025 MCP spec [^397^][^399^].
- **Distribution**: NuGet for .NET (local stdio tools), npm for TypeScript, MCPB bundles for Claude Desktop, and the official MCP Registry [^357^][^358^][^435^].

---

## 2. Technical Architecture & Components

### 2.1 MCP Protocol Overview

MCP is built on JSON-RPC 2.0 with three core primitives [^377^][^379^]:

| Primitive | Purpose | JSON-RPC Method |
|-----------|---------|-----------------|
| **Tools** | Functions the AI can invoke | `tools/list`, `tools/call` |
| **Resources** | Read-only data exposed via URIs | `resources/list`, `resources/read` |
| **Prompts** | Reusable prompt templates | `prompts/list`, `prompts/get` |

### 2.2 Connection Lifecycle

All MCP connections follow a three-phase handshake [^377^][^378^]:

1. **Initialize Request** (client -> server):
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "clientInfo": { "name": "ExampleHostApp", "version": "1.2.0" },
    "capabilities": { "tools": {}, "resources": {}, "prompts": {} }
  }
}
```

2. **Initialize Response** (server -> client):
```json
{
  "jsonrpc": "2.0",
  "id": "1",
  "result": {
    "protocolVersion": "2024-11-05",
    "capabilities": { "tools": {}, "resources": {} },
    "serverInfo": { "name": "SampleMCPServer", "version": "0.1.0" }
  }
}
```

3. **Initialized Notification** (client -> server, no response needed):
```json
{ "jsonrpc": "2.0", "method": "initialized" }
```

### 2.3 SDK Package Architecture

**.NET SDK Packages** [^410^][^413^]:

| Package | Purpose | Dependencies |
|---------|---------|-------------|
| `ModelContextProtocol.Core` | Client + low-level server APIs, minimal deps | Standalone |
| `ModelContextProtocol` | Hosting, DI, attribute-based discovery | `ModelContextProtocol.Core` |
| `ModelContextProtocol.AspNetCore` | HTTP-based MCP servers | `ModelContextProtocol` |

**TypeScript SDK** [^355^][^412^]:

| Package | Purpose |
|---------|---------|
| `@modelcontextprotocol/sdk` | Core SDK with server/client classes |
| `zod` | Runtime schema validation (peer dependency) |
| `express` | For HTTP transport (optional) |

---

## 3. .NET Implementation Details

### 3.1 Prerequisites

- **.NET 10.0 SDK** or later is required for `Microsoft.McpServer.ProjectTemplates` [^53^]
- Visual Studio 2022 or VS Code with C# Dev Kit [^53^]
- For NuGet publishing: NuGet.org account [^371^]

### 3.2 Creating a .NET MCP Server (Project Template)

Install the template and create a project [^53^][^435^]:

```bash
# Install the project template (requires .NET 10 SDK)
dotnet new install Microsoft.McpServer.ProjectTemplates

# Create a new MCP server project
dotnet new mcpserver -n SampleMcpServer

# Create with specific transport type
dotnet new mcpserver -n MyHttpServer --transport http
dotnet new mcpserver -n MyStdioServer --transport stdio
```

### 3.3 Stdio Server Program.cs

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr (critical for stdio transport!)
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register MCP server with stdio transport and assembly scanning
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
```
[^382^][^433^][^434^]

### 3.4 HTTP Server Program.cs (ASP.NET Core)

```csharp
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()          // HTTP/Streamable HTTP transport
    .WithToolsFromAssembly();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapMcp("/mcp");  // Mount MCP endpoint at /mcp
app.Run();
```
[^409^][^437^]

### 3.5 Defining Tools

Tools are defined as **static methods** in classes decorated with `[McpServerToolType]` [^400^][^381^][^437^]:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class RandomNumberTools
{
    [McpServerTool(Name = "get_random_number", Title = "Get Random Number")]
    [Description("Generates a random number between min and max values.")]
    public int GetRandomNumber(
        [Description("Minimum value (inclusive)")] int min = 0,
        [Description("Maximum value (exclusive)")] int max = 100)
    {
        return Random.Shared.Next(min, max);
    }
}
```

**Tool with dependency injection** [^400^][^475^]:

```csharp
[McpServerToolType]
public class MetricTools
{
    private readonly ILogger<MetricTools> _logger;
    private readonly IMyService _service;

    public MetricTools(ILogger<MetricTools> logger, IMyService service)
    {
        _logger = logger;
        _service = service;
    }

    [McpServerTool(
        Name = "search_code_metrics",
        ReadOnly = true,        // Does not modify environment
        Idempotent = false,     // Results may vary between calls
        Destructive = false,    // Not destructive
        OpenWorld = false)]     // Does not depend on external sources
    [Description("Search code metrics for code elements.")]
    public async Task<SearchResult> SearchCodeMetrics(
        [Description("Cursor for pagination; 0 starts from beginning")] int cursor,
        [Description("Max items per page (1-100)")] int pageSize,
        [Description("Filter by metric name. Possible values: LOC, CC, MI")] string? metricName,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Invoking search_code_metrics...");

        // Your logic here
        var results = await _service.SearchAsync(cursor, pageSize, metricName, cancellationToken);
        return results;
    }
}
```

### 3.6 Defining Resources

Resources provide read-only data via URIs [^475^][^434^]:

```csharp
[McpServerResourceType]
public class ProjectResources
{
    private readonly ILogger<ProjectResources> _logger;

    public ProjectResources(ILogger<ProjectResources> logger)
    {
        _logger = logger;
    }

    // Static resource (single URI)
    [McpServerResource(UriTemplate = "project://readme")]
    [Description("Returns the project README file content")]
    public string GetReadme()
    {
        _logger.LogInformation("GetReadme called");
        return "# Project Documentation\n\nThis is the README.";
    }

    // Dynamic resource with template
    [McpServerResource(UriTemplate = "project://files/{filename}")]
    [Description("Returns a specific project file by name")]
    public async Task<BlobResourceContents> GetFile(string filename)
    {
        var bytes = await File.ReadAllBytesAsync(filename);
        return new BlobResourceContents
        {
            Uri = $"project://files/{filename}",
            Blob = Convert.ToBase64String(bytes),
            MimeType = "application/octet-stream"
        };
    }
}
```

Register resources in Program.cs:
```csharp
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();  // Scan for [McpServerResourceType] classes
```

### 3.7 Defining Prompts

Prompts are reusable templates [^480^][^434^]:

```csharp
[McpServerPromptType]
public class CodeReviewPrompt
{
    private readonly ILogger<CodeReviewPrompt> _logger;

    public CodeReviewPrompt(ILogger<CodeReviewPrompt> logger)
    {
        _logger = logger;
    }

    [McpServerPrompt(Name = "code_review", Title = "Code Review Prompt")]
    [Description("Generates a code review prompt for a given file.")]
    public IReadOnlyCollection<ChatMessage> Format(
        [Description("The file path to review.")] string filePath)
    {
        _logger.LogInformation("Generating code review prompt for: {FilePath}", filePath);
        return new[]
        {
            new ChatMessage(ChatRole.User,
                $"Please review the code in file '{filePath}'. " +
                "Check for: code quality, security issues, performance concerns, and adherence to best practices.")
        };
    }
}
```

### 3.8 Tool Annotations (Metadata)

The .NET SDK supports tool annotations that tell the LLM about tool behavior [^400^][^264^]:

```csharp
[McpServerTool(
    Name = "delete_repository",
    ReadOnly = false,       // Tool modifies data
    Destructive = true,     // Changes are irreversible
    Idempotent = true,      // Same input always produces same result
    OpenWorld = false)]     // Tool does not depend on external data
```

| Annotation | Meaning |
|------------|---------|
| `ReadOnly` | Tool does not modify the environment |
| `Destructive` | Tool performs irreversible changes |
| `Idempotent` | Same input always produces same output |
| `OpenWorld` | Tool depends on dynamic/unpredictable sources |
| `Title` | Human-readable title shown in UI |

### 3.9 Key NuGet Package References (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- For publishing as NuGet tool -->
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>my-mcp-server</ToolCommandName>
    <PackageId>MyUsername.MyMcpServer</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <!-- Main MCP SDK package for stdio servers -->
    <PackageReference Include="ModelContextProtocol" Version="1.2.0" />
    <!-- For HTTP-based servers, add: -->
    <PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.2.0" />
  </ItemGroup>
</Project>
```
[^415^][^440^][^53^]

---

## 4. TypeScript Implementation Details

### 4.1 Project Setup

```bash
mkdir my-mcp-server && cd my-mcp-server
npm init -y
npm install @modelcontextprotocol/sdk zod
npm install -D typescript @types/node
```
[^359^][^416^]

**tsconfig.json**:
```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "Node16",
    "moduleResolution": "Node16",
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "declaration": true
  },
  "include": ["src/**/*"]
}
```

**package.json**:
```json
{
  "name": "my-mcp-server",
  "version": "1.0.0",
  "type": "module",
  "main": "dist/index.js",
  "bin": { "my-mcp-server": "dist/index.js" },
  "scripts": {
    "build": "tsc",
    "start": "node dist/index.js"
  }
}
```

### 4.2 Stdio Server Implementation

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

const server = new McpServer({
  name: "my-mcp-server",
  version: "1.0.0"
});

// Register a tool with Zod validation
server.registerTool(
  "search_code",
  {
    title: "Search Code",
    description: "Search the codebase for matching files and symbols.",
    inputSchema: {
      query: z.string()
        .min(1, "Query is required")
        .max(200, "Query too long")
        .describe("Search query string to match against code"),
      language: z.enum(["csharp", "typescript", "python", "all"])
        .default("all")
        .describe("Filter by programming language"),
      maxResults: z.number()
        .int()
        .min(1)
        .max(50)
        .default(10)
        .describe("Maximum number of results to return")
    },
    annotations: {
      readOnlyHint: true,
      idempotentHint: true,
      destructiveHint: false
    }
  },
  async ({ query, language, maxResults }) => {
    try {
      // Your search logic here
      const results = await performCodeSearch(query, language, maxResults);

      return {
        content: [{
          type: "text",
          text: JSON.stringify(results, null, 2)
        }],
        structuredContent: results  // Modern pattern for typed data
      };
    } catch (error) {
      return {
        content: [{
          type: "text",
          text: `Error: ${error instanceof Error ? error.message : String(error)}`
        }],
        isError: true
      };
    }
  }
);

// Start the server
const transport = new StdioServerTransport();
await server.connect(transport);
console.error("MCP Server running on stdio");  // Use stderr for logging!
```
[^355^][^359^][^412^]

### 4.3 HTTP (Streamable HTTP) Server Implementation

```typescript
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import express from "express";
import cors from "cors";
import { randomUUID } from "node:crypto";

const app = express();
app.use(express.json());
app.use(cors({
  origin: "*",
  exposedHeaders: ["Mcp-Session-Id"]
}));

const server = new McpServer({
  name: "http-mcp-server",
  version: "1.0.0"
});

// Register tools, resources, prompts...
server.registerTool("echo", {
  title: "Echo Tool",
  description: "Echoes back the input",
  inputSchema: { message: z.string().describe("Message to echo") }
}, async ({ message }) => ({
  content: [{ type: "text", text: message }]
}));

// Session management for HTTP transport
const transports: Map<string, StreamableHTTPServerTransport> = new Map();

app.post("/mcp", async (req, res) => {
  const sessionId = req.headers["mcp-session-id"] as string | undefined;
  let transport: StreamableHTTPServerTransport;

  if (sessionId && transports.has(sessionId)) {
    transport = transports.get(sessionId)!;
  } else if (!sessionId && isInitializeRequest(req.body)) {
    transport = new StreamableHTTPServerTransport({
      sessionIdGenerator: () => randomUUID(),
      onsessioninitialized: (sid) => { transports.set(sid, transport); }
    });
    transport.onclose = () => {
      if (transport.sessionId) transports.delete(transport.sessionId);
    };
    await server.connect(transport);
  } else {
    res.status(400).json({
      jsonrpc: "2.0",
      error: { code: -32000, message: "Bad Request" },
      id: null
    });
    return;
  }

  await transport.handleRequest(req, res, req.body);
});

app.listen(3000, () => console.error("MCP HTTP Server on port 3000"));
```
[^355^][^476^]

### 4.4 Registering Resources (TypeScript)

```typescript
import { ResourceTemplate } from "@modelcontextprotocol/sdk/server/mcp.js";

// Static resource
server.registerResource("app-config", "config://app", {
  title: "Application Configuration"
}, async (uri) => ({
  contents: [{ uri: uri.href, text: '{ "theme": "dark" }' }]
}));

// Dynamic resource with URI template
server.registerResource(
  "code-file",
  new ResourceTemplate("code://files/{filepath}", { list: undefined }),
  { title: "Code File", mimeType: "text/plain" },
  async (uri, { filepath }) => {
    const content = await readFile(filepath, "utf-8");
    return {
      contents: [{ uri: uri.href, text: content, mimeType: "text/plain" }]
    };
  }
);
```
[^412^][^438^]

### 4.5 Registering Prompts (TypeScript)

```typescript
server.registerPrompt(
  "code_review",
  {
    title: "Code Review Prompt",
    description: "Generates a code review prompt for a given file",
    argsSchema: {
      filePath: z.string().describe("Path to the file to review"),
      focusAreas: z.array(z.string()).optional()
        .describe("Specific areas to focus on")
    }
  },
  async ({ filePath, focusAreas }) => ({
    messages: [{
      role: "user",
      content: {
        type: "text",
        text: `Please review the code in ${filePath}.` +
          (focusAreas ? ` Focus on: ${focusAreas.join(", ")}` : "")
      }
    }]
  })
);
```
[^102^][^412^]

---

## 5. Transport Configuration

### 5.1 Stdio Transport

**Best for**: Local development, single-user tools, VS Code integration, Claude Desktop [^40^][^354^]

| Aspect | Details |
|--------|---------|
| Network | Local only |
| Clients | Single client |
| Setup | Zero configuration |
| Security | Inherently secure (process-based) |
| Logging | MUST use stderr, not stdout |
| Performance | Lower latency |

**Critical rule**: Server MUST NOT write anything to stdout except valid MCP messages. All logs MUST go to stderr [^40^][^374^].

**.NET stdio setup**:
```csharp
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
```

**TypeScript stdio setup**:
```typescript
const transport = new StdioServerTransport();
await server.connect(transport);
```

### 5.2 Streamable HTTP Transport

**Best for**: Remote servers, multi-client, cloud deployment, Claude.ai Custom Connectors [^354^][^356^]

| Aspect | Details |
|--------|---------|
| Network | Remote-capable |
| Clients | Multiple concurrent |
| Endpoint | Single `/mcp` (POST for requests, GET for SSE stream) |
| Session | Managed via `Mcp-Session-Id` header |
| Setup | Requires HTTP server setup |

**.NET HTTP setup**:
```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();
// ...
app.MapMcp("/mcp");
```

**TypeScript HTTP setup**: See Section 4.3 above.

### 5.3 Transport Comparison

| Feature | Stdio | Streamable HTTP | SSE (Legacy) |
|---------|-------|-----------------|-------------|
| Network | Local only | Remote | Remote |
| Concurrent Clients | 1 | Many | Many |
| Production Ready | Fails under load | **Recommended** | Deprecated |
| Endpoint Count | stdin/stdout | 1 (`/mcp`) | 2 (`/sse` + `/message`) |
| Claude Desktop | Native | Needs bridge | Needs bridge |
| Claude.ai | Not supported | **Native** | Not supported |
| ChatGPT | Not supported | Supported | Not supported |
[^354^][^356^]

### 5.4 Critical: stdout vs stderr for stdio Transport

When using stdio transport, **stdout is reserved exclusively for JSON-RPC messages**. Any debug logging or console output to stdout will silently break the protocol [^374^].

```typescript
// CORRECT: Use stderr for logging
console.error("Server started");

// WRONG: This will break the MCP protocol!
console.log("Server started");  // Never do this in stdio mode!
```

---

## 6. Tool Discovery & Invocation Protocol

### 6.1 Tool Discovery (`tools/list`)

The client sends a `tools/list` JSON-RPC request after initialization [^377^][^407^]:

**Request**:
```json
{ "jsonrpc": "2.0", "id": 1, "method": "tools/list" }
```

**Response**:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "get_random_number",
        "description": "Generates a random number between min and max values.",
        "inputSchema": {
          "type": "object",
          "properties": {
            "min": { "type": "integer", "description": "Minimum value (inclusive)" },
            "max": { "type": "integer", "description": "Maximum value (exclusive)" }
          }
        },
        "annotations": {
          "readOnlyHint": true,
          "idempotentHint": false,
          "destructiveHint": false
        }
      }
    ]
  }
}
```

### 6.2 Tool Invocation (`tools/call`)

**Request**:
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_random_number",
    "arguments": { "min": 1, "max": 100 }
  }
}
```

**Success Response**:
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      { "type": "text", "text": "42" }
    ],
    "isError": false
  }
}
```

**Error Response**:
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "error": {
    "code": -32602,
    "message": "Invalid parameters",
    "data": { "details": "Parameter 'min' must be less than 'max'" }
  }
}
```

### 6.3 Return Content Types

Tool results can include multiple content types [^412^][^355^]:

```typescript
// Text content (most common)
return {
  content: [{ type: "text", text: "Hello, world!" }]
};

// Image content
return {
  content: [{
    type: "image",
    data: base64EncodedImage,
    mimeType: "image/png"
  }]
};

// Multiple content blocks
return {
  content: [
    { type: "text", text: "# Analysis Results" },
    { type: "text", text: JSON.stringify(data, null, 2) },
    { type: "image", data: chartBase64, mimeType: "image/png" }
  ]
};

// Error result
return {
  content: [{ type: "text", text: "Error: File not found" }],
  isError: true
};
```

---

## 7. Authentication Patterns

### 7.1 Three Authentication Approaches

| Approach | Best For | Complexity | Spec Compliant |
|----------|----------|------------|---------------|
| **Bearer Token** | Personal/team tools, internal servers | 5 min setup | No (acceptable for private) |
| **Cloudflare Access** | Team access with SSO | 15 min setup | No (proxy-level) |
| **OAuth 2.1 + PKCE** | Public servers, user-facing integrations | Several hours | **Yes** |
[^397^][^399^]

### 7.2 Bearer Token Authentication (Simplest)

For internal/team MCP servers, a static Bearer token is sufficient [^397^]:

**Server-side** (TypeScript/Express):
```typescript
app.post("/mcp", async (req, res) => {
  const authHeader = req.headers["authorization"];
  const expectedToken = process.env.MCP_SECRET_TOKEN;

  if (!authHeader || authHeader !== `Bearer ${expectedToken}`) {
    res.status(401).json({
      error: "Unauthorized",
      headers: { "WWW-Authenticate": "Bearer" }
    });
    return;
  }
  // ... handle MCP request
});
```

**Client-side** (Claude Desktop config):
```json
{
  "mcpServers": {
    "my-server": {
      "url": "https://my-server.example.com/mcp",
      "headers": {
        "Authorization": "Bearer your-secret-token-here"
      }
    }
  }
}
```

### 7.3 OAuth 2.1 + PKCE (Full Spec)

For public remote MCP servers, the November 2025 spec mandates OAuth 2.1 with PKCE [^397^][^399^]:

**Required endpoints**:

1. **Protected Resource Metadata** (`GET /.well-known/oauth-protected-resource`):
```json
{
  "resource": "https://your-mcp-server.com",
  "authorization_servers": ["https://your-auth-server.com"],
  "scopes_supported": ["mcp:read", "mcp:write"],
  "bearer_methods_supported": ["header"]
}
```

2. **Authorization Server Metadata** (`GET /.well-known/oauth-authorization-server`):
```json
{
  "issuer": "https://your-auth-server.com",
  "authorization_endpoint": "https://your-auth-server.com/authorize",
  "token_endpoint": "https://your-auth-server.com/token",
  "response_types_supported": ["code"],
  "code_challenge_methods_supported": ["S256"]
}
```

**MCP spec requirements** [^397^]:
- PKCE S256 mandatory (no `plain` method)
- HTTPS everywhere
- Audience validation (`aud` claim in tokens)
- No token passthrough to downstream APIs

### 7.4 .NET MCP Server with Auth0/OAuth

For ASP.NET Core MCP servers, use standard ASP.NET Core auth [^437^][^418^]:

```csharp
builder.Services
    .AddMcpServer()
    .AddAuthorizationFilters()   // Enable authorization
    .WithHttpTransport()
    .WithToolsFromAssembly();

// ...
app.MapMcp("/mcp").RequireAuthorization();
```

For reading custom HTTP headers in tools [^443^]:
```csharp
// Register HttpContextAccessor
builder.Services.AddHttpContextAccessor();

[McpServerToolType]
public class AuthTool(IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool, Description("Echoes back the authenticated user.")]
    public async Task<string> EchoUser()
    {
        var context = httpContextAccessor.HttpContext;
        var user = context?.User?.Identity?.Name ?? "anonymous";
        return $"Hello, {user}!";
    }
}
```

### 7.5 Azure AD / Microsoft Entra Integration

For Azure-hosted MCP servers [^398^][^353^]:

```python
# Protected Resource Metadata for Azure AD
{
  "resource": "https://your-mcp-server.azurecontainerapps.io",
  "bearer_methods_supported": ["header"],
  "authorization_servers": [
    "https://login.microsoftonline.com/{tenantId}/v2.0"
  ],
  "scopes_supported": [
    "api://{clientId}/access_as_user",
    "api://{clientId}/MCP.Tools"
  ]
}
```

---

## 8. Packaging & Distribution

### 8.1 .NET NuGet Distribution

For .NET MCP servers intended for local (stdio) use, NuGet is the recommended distribution channel [^435^][^441^]:

**Requirements**:
- Set `PackAsTool=true` in `.csproj`
- Include `server.json` in `.mcp/` folder for discovery metadata
- Add MCP package type tag

```xml
<PropertyGroup>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>my-mcp-server</ToolCommandName>
  <PackageId>MyOrg.MyMcpServer</PackageId>
  <PackageType>DotNetCliTool</PackageType>
  <Version>1.0.0</Version>
</PropertyGroup>
```

**server.json** (in `.mcp/` folder) [^435^]:
```json
{
  "$schema": "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json",
  "name": "io.github.myorg/my-mcp-server",
  "version": "1.0.0",
  "description": "MCP server for code indexing and repository operations.",
  "packages": [{
    "registryType": "nuget",
    "registryBaseUrl": "https://api.nuget.org",
    "identifier": "MyOrg.MyMcpServer",
    "version": "1.0.0",
    "transport": { "type": "stdio" },
    "environmentVariables": [{
      "name": "API_KEY",
      "variables": {
        "api_key": { "description": "API key for external service", "isRequired": true, "isSecret": true }
      }
    }]
  }]
}
```

**Pack and publish**:
```bash
dotnet pack -c Release
dotnet nuget push bin/Release/*.nupkg --api-key <API_KEY> --source https://api.nuget.org/v3/index.json
```
[^53^][^435^][^436^]

### 8.2 npm Distribution (TypeScript)

```json
{
  "name": "my-mcp-server",
  "version": "1.0.0",
  "type": "module",
  "bin": { "my-mcp-server": "dist/index.js" },
  "files": ["dist/", "README.md"],
  "scripts": {
    "build": "tsc",
    "prepublishOnly": "npm run build"
  },
  "keywords": ["mcp", "mcp-server", "ai", "model-context-protocol"]
}
```

```bash
npm publish --access public
```
[^358^]

### 8.3 MCPB (MCP Bundles) for Claude Desktop

MCPB is Anthropic's one-click install format [^357^]:

```bash
npm install -g @anthropic-ai/mcpb
mcpb validate manifest.json
mcpb pack . my-server.mcpb
```

Attach `.mcpb` files to GitHub Releases for one-click installation.

### 8.4 MCP Registry

The official MCP Registry indexes servers from NuGet packages [^436^]:
- Package README must contain `<!-- mcp-name: [name from server.json] -->`
- NuGet.org provides a tailored MCP browsing experience
- Registry URL: Via Microsoft official channels

### 8.5 Distribution Decision Guide

| Scenario | Distribution Method |
|----------|-------------------|
| .NET local tool (stdio) | NuGet (`PackAsTool`) |
| TypeScript CLI tool | npm + GitHub Releases |
| Claude Desktop users | MCPB bundle |
| Remote HTTP server | Container (Docker/ACA/Cloud Run) |
| Public registry listing | MCP Registry via NuGet |
[^357^][^358^][^435^][^441^]

---

## 9. Error Handling & Logging

### 9.1 Structured Logging Best Practices

MCP servers should implement structured logging for observability [^368^][^369^]:

**Key areas to log**:
1. Connection/authentication events
2. Tool invocation requests (tool name, parameters, duration)
3. Errors with stack traces
4. Session lifecycle events

**.NET logging configuration**:
```csharp
builder.Logging.AddConsole(options =>
{
    // CRITICAL for stdio: log to stderr, not stdout
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
```
[^382^][^400^]

**TypeScript structured logging**:
```typescript
import pino from "pino";

const logger = pino({
  level: process.env.LOG_LEVEL || "info",
  formatters: {
    level: (label) => ({ level: label.toUpperCase() }),
    bindings: () => ({ pid: process.pid })
  }
});

// Log tool invocations
logger.info({ tool: "search_code", query, durationMs: elapsed, resultCount }, "Tool executed");

// Log errors
logger.error({ err: error, tool: name, arguments: args }, "Tool execution failed");
```
[^416^]

### 9.2 Error Handling Patterns

**In tool implementations**:
```typescript
server.registerTool("risky_operation", {
  title: "Risky Operation",
  inputSchema: { id: z.string() }
}, async ({ id }) => {
  try {
    const result = await performOperation(id);
    return { content: [{ type: "text", text: result }] };
  } catch (error) {
    // Return error as content (preferred for graceful degradation)
    return {
      content: [{
        type: "text",
        text: `Operation failed: ${error instanceof Error ? error.message : String(error)}`
      }],
      isError: true
    };
  }
});
```
[^412^][^416^]

### 9.3 JSON-RPC Error Codes

| Code | Meaning |
|------|---------|
| `-32700` | Parse error |
| `-32600` | Invalid request |
| `-32601` | Method not found |
| `-32602` | Invalid parameters |
| `-32603` | Internal error |
| `-32000` | Server error (generic) |
[^377^][^407^]

### 9.4 Audit Logging

For security-sensitive MCP servers, implement audit logging [^372^][^369^]:

```typescript
// Log every tool call with full context
function auditLog(toolName: string, args: unknown, userId: string, result: unknown, durationMs: number) {
  logger.info({
    event: "tool_invocation",
    timestamp: new Date().toISOString(),
    userId,
    toolName,
    arguments: sanitizeArgs(args),  // Remove sensitive data
    result: result ? "success" : "error",
    durationMs,
    correlationId: requestContext.get("correlationId")
  });
}
```

---

## 10. VS Code Extension Integration

### 10.1 Registering MCP Server in a VS Code Extension

VS Code provides the `vscode.lm.registerMcpServerDefinitionProvider` API [^264^][^370^]:

```typescript
import * as vscode from "vscode";

export function activate(context: vscode.ExtensionContext) {
  const didChangeEmitter = new vscode.EventEmitter<void>();

  const provider = vscode.lm.registerMcpServerDefinitionProvider("myProvider", {
    onDidChangeMcpServerDefinitions: didChangeEmitter.event,

    provideMcpServerDefinitions: async () => {
      const servers: vscode.McpServerDefinition[] = [];

      // stdio server (local)
      servers.push(new vscode.McpStdioServerDefinition({
        label: "My Code Tools",
        command: "node",
        args: ["/path/to/dist/server.js"],
        env: { API_KEY: "" },
        version: "1.0.0"
      }));

      // HTTP server (remote)
      servers.push(new vscode.McpHttpServerDefinition({
        label: "My Remote Tools",
        uri: vscode.Uri.parse("http://localhost:3000"),
        headers: { "API_VERSION": "1.0.0" },
        version: "1.0.0"
      }));

      return servers;
    },

    resolveMcpServerDefinition: async (server: vscode.McpServerDefinition) => {
      if (server.label === "My Code Tools") {
        const apiKey = await vscode.window.showInputBox({
          prompt: "Enter API key for My Code Tools",
          password: true
        });
        if (apiKey) {
          (server as vscode.McpStdioServerDefinition).env.API_KEY = apiKey;
        }
      }
      return server;
    }
  });

  context.subscriptions.push(provider);
}
```
[^264^][^370^]

### 10.2 Embedding MCP Server Inside VS Code Extension

Best practice: Ship the MCP server as part of the VS Code extension [^370^]:

```typescript
// Extension activation
const MCP_SERVER_MODULE = ["dist", "mcp", "server.mjs"];

export function registerMcpServer(context: vscode.ExtensionContext) {
  const provider = vscode.lm.registerMcpServerDefinitionProvider("myext.mcp", {
    provideMcpServerDefinitions: async () => {
      const mcpServerPath = vscode.Uri.joinPath(
        context.extensionUri, ...MCP_SERVER_MODULE
      ).fsPath;

      return [new vscode.McpStdioServerDefinition({
        label: "My Extension MCP",
        command: "node",
        args: [mcpServerPath],
        env: { CONTENT_DIR: workspaceRoot },
        version: context.extension.packageJSON.version
      })];
    },
    resolveMcpServerDefinition: async (server) => server
  });

  context.subscriptions.push(provider);
}
```

**Benefits** [^370^]:
- Single codebase (TypeScript)
- Versioning tied to extension updates
- No separate installation required
- Scoped availability (only when extension is active)

### 10.3 VS Code mcp.json Configuration (for Testing)

```json
{
  "servers": {
    "MyMcpServer": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "MyMcpServer.csproj"],
      "env": {
        "WEATHER_CHOICES": "sunny,humid,freezing"
      }
    }
  }
}
```
[^53^][^382^]

---

## 11. Limitations & Gotchas

### 11.1 .NET SDK Limitations

1. **Template requires .NET 10 SDK**: `Microsoft.McpServer.ProjectTemplates` requires .NET 10.0 SDK or later [^53^]
2. **Preview status**: Packages are in preview - APIs may change [^410^][^411^]
3. **HTTP endpoint registration**: `MapMcp()` may not register endpoints correctly in some configurations. Ensure `WithHttpTransport()` is called before `MapMcp()` [^477^]
4. **NuGet stdio only**: NuGet-distributed MCP servers support stdio transport only (not HTTP) [^53^]
5. **Native AOT**: For Native AOT publishing, ensure all reflection-based discovery is pre-compiled [^482^]

### 11.2 TypeScript SDK Limitations

1. **API deprecation**: Old APIs like `server.tool()`, `server.setRequestHandler()` are deprecated. Use `server.registerTool()`, `server.registerResource()`, `server.registerPrompt()` [^412^]
2. **Zod version**: The SDK requires Zod v3 specifically [^416^]
3. **Node.js version**: Requires Node.js 20 or later [^355^]
4. **stdout trap**: `console.log()` to stdout silently breaks stdio protocol. Always use `console.error()` [^374^]

### 11.3 Protocol-Level Gotchas

1. **Stdio concurrency**: stdio fails under concurrent load (20 of 22 requests failed with 20 simultaneous connections in testing) [^354^]
2. **Session management**: HTTP transport requires explicit session tracking via `Mcp-Session-Id` [^355^]
3. **Message framing**: JSON-RPC messages MUST NOT contain embedded newlines [^40^]
4. **Tool parameter types**: Keep parameters simple (strings, numbers, booleans). Complex objects confuse LLMs [^400^]

### 11.4 Authentication Gotchas

1. **Bearer token storage**: Stored in plaintext in client config files. Never commit to version control [^397^]
2. **OAuth complexity**: Writing a spec-compliant OAuth authorization server from scratch is a multi-month project. Delegate to existing providers (Auth0, Keycloak, Azure AD) [^396^]
3. **Audience validation**: Must validate `aud` claim to prevent token replay across servers [^397^]

---

## 12. Recommendations for Prototype

### 12.1 Recommended Architecture for Code Indexing / PR / Q&A

```
+---------------------------------------------------------+
|                    VS Code Extension                      |
|  +--------------------------------------------------+   |
|  |  Copilot Chat / Agent Mode                        |   |
|  |  + MCP Client (built-in)                         |   |
|  +---------+----------------------------------------+   |
|            | stdio transport                          |
+------------+------------------------------------------+
             |
+------------v------------------------------------------+
|            MCP Server (.NET or TypeScript)             |
|  +--------------------------------------------------+ |
|  |  Tools:                                           | |
|  |   - search_code(query, language, maxResults)     | |
|  |   - get_file_content(filepath)                   | |
|  |   - create_pr(title, description, branch, files) | |
|  |   - get_repository_qa(question)                  | |
|  |   - index_repository(path)                       | |
|  +--------------------------------------------------+ |
+------------+------------------------------------------+
             |
             v
+------------+------------------------------------------+
|         External Services                              |
|  - GitHub API (PR creation)                            |
|  - Local file system (code indexing)                   |
|  - Vector database (semantic search)                   |
+--------------------------------------------------------+
```

### 12.2 Technology Stack Recommendation

| Component | Recommendation | Rationale |
|-----------|---------------|-----------|
| **Language** | TypeScript | Best SDK support, VS Code ecosystem, Zod validation |
| **Transport** | stdio | Simplest for VS Code integration, zero config |
| **Server SDK** | `@modelcontextprotocol/sdk` | Official Tier-1 SDK, 66M+ downloads |
| **Validation** | Zod | Runtime type safety, auto JSON Schema generation |
| **Logging** | pino | Fast structured logging to stderr |
| **Distribution** | npm | Standard for TypeScript/Node.js tools |

### 12.3 Prototype Tool Definitions

```typescript
// Tool: Search code across the repository
server.registerTool("search_code", {
  title: "Search Code",
  description: "Search the repository for code matching a query. Returns file paths, line numbers, and matching snippets.",
  inputSchema: {
    query: z.string().describe("Search query string"),
    filePattern: z.string().optional().describe("Glob pattern to filter files (e.g., '*.cs')"),
    maxResults: z.number().int().max(50).default(20).describe("Maximum results")
  },
  annotations: { readOnlyHint: true, idempotentHint: true }
}, async ({ query, filePattern, maxResults }) => { /* ... */ });

// Tool: Get file content
server.registerTool("get_file_content", {
  title: "Get File Content",
  description: "Read the full content of a file.",
  inputSchema: {
    filepath: z.string().describe("Relative path to the file")
  },
  annotations: { readOnlyHint: true }
}, async ({ filepath }) => { /* ... */ });

// Tool: Create a PR
server.registerTool("create_pull_request", {
  title: "Create Pull Request",
  description: "Create a new pull request on GitHub.",
  inputSchema: {
    title: z.string().describe("PR title"),
    description: z.string().describe("PR description (supports Markdown)"),
    branchName: z.string().describe("Name for the new branch"),
    files: z.array(z.object({
      path: z.string(),
      content: z.string()
    })).describe("Files to include in the PR")
  },
  annotations: { readOnlyHint: false, destructiveHint: false }
}, async ({ title, description, branchName, files }) => { /* ... */ });

// Tool: Repository Q&A (uses indexed codebase)
server.registerTool("ask_repository", {
  title: "Ask Repository",
  description: "Ask a question about the codebase using semantic search over indexed code.",
  inputSchema: {
    question: z.string().describe("Natural language question about the code")
  },
  annotations: { readOnlyHint: true }
}, async ({ question }) => { /* ... */ });
```

### 12.4 Implementation Checklist

- [ ] Set up TypeScript project with `@modelcontextprotocol/sdk` and `zod`
- [ ] Implement `McpServer` with `StdioServerTransport`
- [ ] Define tools with Zod schemas and clear descriptions
- [ ] Implement tool handlers with proper error handling
- [ ] Add structured logging to stderr (pino)
- [ ] Test with VS Code mcp.json configuration
- [ ] Package as npm module with `bin` entry
- [ ] Add integration with GitHub API for PR creation
- [ ] Add code indexing (tree-sitter or AST-based)
- [ ] Add semantic search (vector embeddings)
- [ ] Write comprehensive README with configuration examples

---

## 13. Sources & References

| Citation | Source | Description |
|----------|--------|-------------|
| [^40^] | modelcontextprotocol.io | Official MCP Transport Specification |
| [^53^] | Microsoft Learn | Create a minimal MCP server using C# and publish to NuGet |
| [^102^] | CodeSignal Learn | Exploring MCP Primitives (Tools, Resources, Prompts) |
| [^264^] | Visual Studio Code Docs | MCP developer guide for VS Code extensions |
| [^353^] | Microsoft Foundry | Set Up MCP Server Authentication |
| [^354^] | Apigene.ai | MCP SSE vs Stdio: Transport Options Explained |
| [^355^] | Agentailor Blog | The MCP TypeScript SDK: A Complete Guide |
| [^356^] | Roo Code Docs | MCP Server Transports: STDIO, Streamable HTTP & SSE |
| [^357^] | GitHub/google-research-mcp | MCP Distribution Channels Guide |
| [^358^] | Speakeasy | Distribute your MCP server guide |
| [^359^] | Medium/thecraftman | Build Your First MCP Server with TypeScript |
| [^361^] | GitHub/dotnet/extensions | MCP Server .NET docs reference discussion |
| [^368^] | Milvus AI Quick Reference | Debug logs for MCP servers |
| [^369^] | Red Hat Blog | MCP security: Logging and runtime security measures |
| [^370^] | Ken Muse Blog | Adding an MCP Server to a VS Code Extension |
| [^371^] | Medium/vik-sharma | Publishing MCP Server to NuGet |
| [^372^] | Medium/bytebridge | Implementing Audit Logging and Retention in MCP |
| [^374^] | Reddit r/mcp | console.log() Silently Breaks stdio MCP |
| [^376^] | TrueFoundry Blog | Enterprise MCP access control |
| [^377^] | Pradeepl Blog | MCP Protocol Mechanics and Architecture |
| [^378^] | Portkey.ai Blog | Complete MCP JSON-RPC Reference Guide |
| [^379^] | Medium/sadikkhadeer | Model Context Protocol Fundamentals |
| [^381^] | CCD Akademie | Implement MCP Server in C# |
| [^382^] | Ottorino Bruni Blog | Build MCP Server in C#/.NET with Real Example |
| [^396^] | Prefect Blog | MCP OAuth: How OAuth 2.1 Works in MCP |
| [^397^] | MCP Playground Blog | How to Add Authentication to Your MCP Server |
| [^398^] | Microsoft DevBlogs | Building a Secure MCP Server with OAuth 2.1 and Azure AD |
| [^399^] | Red Hat Blog | MCP security: Authentication and authorization |
| [^400^] | NDepend Blog | Developing an MCP Server with C#: A Complete Guide |
| [^406^] | CodeSignal Learn | Creating Custom MCP Tools |
| [^407^] | Medium/kuldeepsingh382002 | JSON-RPC Explained for MCP Developers |
| [^409^] | Microsoft Learn | Deploy a .NET MCP server to Azure Container Apps |
| [^410^] | NuGet.org | ModelContextProtocol 1.2.0 package |
| [^411^] | NuGet.org | ModelContextProtocol.AspNetCore 1.2.0 package |
| [^412^] | GitHub/anthropics/skills | Node/TypeScript MCP Server Implementation Guide |
| [^413^] | CSharp.SDK.ModelContextProtocol.io | Getting Started with MCP C# SDK |
| [^415^] | Uno Platform Blog | Build MCP Servers in C# for AI-Driven Development |
| [^416^] | rebeccamdeprey.com | Build a Secure MCP Server in TypeScript from Scratch |
| [^418^] | Medium/octelys | MCP Server with OAuth Authentication in ASP.NET Core |
| [^433^] | Jamie Maguire Blog | Building and Debugging Your First MCP Server in .NET |
| [^434^] | Dometrain Blog | How to Build and Consume MCP Servers in .NET |
| [^435^] | Anuraj.dev Blog | Publish your .NET MCP Server to NuGet |
| [^436^] | Microsoft Learn | Publish a .NET MCP server to the MCP Registry |
| [^437^] | Auth0 Blog | Secure a C# MCP Server with Auth0 |
| [^438^] | Skywork.ai | In-Depth Analysis of mcp-server-typescript |
| [^475^] | Bart Wullems Blog | Building MCP Resources enabled MCP Server with C# |
| [^476^] | ModelContextProtocol.io | Understanding Authorization in MCP (official) |
| [^477^] | GitHub/modelcontextprotocol/csharp-sdk | MapMcp() endpoint registration issue |
| [^480^] | Dev.to/stormhub | Model context protocol server prompts with semantic kernel |
| [^481^] | GitHub/iamgauravn | MyFirstMCP - Model Context Protocol Server Example |
| [^482^] | Laurent Kempe Blog | SSE-Powered MCP Server with C# and .NET |
| [^483^] | Medium/hemant.mulay | Using Microsoft's C# SDK for MCP to Build MCP Server |

---

*Research compiled from 20+ independent sources across official documentation, Microsoft Learn, GitHub repositories, authoritative blogs, and community resources. All findings include inline citations for traceability.*
