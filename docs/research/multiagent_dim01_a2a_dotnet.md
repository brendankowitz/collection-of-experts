# A2A Protocol .NET Implementation: Deep Dive Research

## 1. Executive Summary

The **Agent-to-Agent (A2A) protocol** is an open standard (originally from Google, now stewarded by the Linux Foundation) that enables AI agents built on different frameworks to discover each other, negotiate communication formats, and exchange messages/tasks. It uses **JSON-RPC 2.0 over HTTP(S)** as its primary transport, with **Server-Sent Events (SSE)** for streaming real-time updates. [^79^] [^78^]

For .NET developers, there are **two primary SDK paths**:

1. **Open-source A2A .NET SDK** (`A2A` and `A2A.AspNetCore` NuGet packages) - the community-driven reference implementation [^350^] [^363^]
2. **Microsoft Agent Framework A2A packages** (`Microsoft.Agents.AI.Hosting.A2A` and `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`) - Microsoft's higher-level abstraction [^344^] [^362^]

Both implement **A2A v1.0** and support .NET 8+. Microsoft Semantic Kernel also provides integration samples that demonstrate wrapping SK agents as A2A-compliant endpoints. [^396^]

**Key NuGet Packages:**
| Package | Version | Purpose |
|---------|---------|---------|
| `A2A` | 1.0.0-preview2+ | Core A2A protocol (client, server, models) [^363^] |
| `A2A.AspNetCore` | 1.0.0-preview2+ | ASP.NET Core routing extensions [^363^] |
| `Microsoft.Agents.AI.Hosting.A2A` | 1.0.0-preview+ | Core hosting logic for MS Agent Framework [^362^] |
| `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` | 1.0.0-preview+ | ASP.NET Core endpoint mapping [^362^] |
| `Microsoft.SemanticKernel.Agents.A2A` | Latest | Semantic Kernel A2A integration (ships with SK) [^396^] |

---

## 2. Technical Architecture & Components

### 2.1 Protocol Stack (3-Layer Model)

The A2A specification defines three distinct layers: [^79^]

```
Layer 1: Canonical Data Model
  Task | Message | AgentCard | Part | Artifact | Extension

Layer 2: A2A Operations
  Send Message | Stream Message | Get Task | List Tasks
  | Cancel Task | Get Agent Card

Layer 3: Protocol Bindings
  JSON-RPC over HTTP | gRPC | HTTP+JSON REST | Custom Bindings
```

### 2.2 Core .NET Classes (A2A SDK)

**Client-side:**
- `A2AClient` - Primary client for A2A requests; supports streaming and non-streaming [^350^]
- `A2ACardResolver` - Discovers agent capabilities via Agent Cards from well-known endpoints [^350^]
- `A2AAgent` - Higher-level wrapper (Semantic Kernel) that converts remote A2A agents into callable SK agents [^484^]

**Server-side:**
- `TaskManager` - Manages complete task lifecycle: creation, updates, cancellation, event streaming [^350^]
- `ITaskStore` / `InMemoryTaskStore` - Task persistence abstraction and in-memory implementation [^350^]

**Core Models:**
- `AgentCard` - Agent metadata, capabilities, endpoint info [^350^]
- `AgentTask` - Task with status, history, artifacts [^350^]
- `Message` - Messages exchanged between agents [^350^]
- `Part` (with `TextPart`, `DataPart`, `FilePart` subtypes) - Content units [^505^] [^114^]
- `Artifact` - Final outputs from task execution [^505^]

### 2.3 Protocol Bindings

A2A v1 supports multiple protocol bindings: [^344^] [^345^]

| Binding | Endpoint Pattern | Content-Type | Use Case |
|---------|------------------|-------------|----------|
| JSON-RPC 2.0 | `POST /rpc` | `application/json` | Legacy, full-featured |
| HTTP+JSON REST | `POST /message:send`, `GET /tasks/{id}` | `application/a2a+json` | **Preferred (default in v1)** |
| SSE Streaming | `POST /message:stream` | `text/event-stream` | Real-time progress |

---

## 3. Implementation Details (with Code Samples)

### 3.1 Minimal A2A Server (Echo Agent)

The simplest A2A-compliant server using the open-source SDK: [^350^] [^508^]

```csharp
using A2A;
using A2A.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var store = new InMemoryTaskStore();
var taskManager = new TaskManager(store);

taskManager.OnSendMessage = async (request, ct) =>
{
    var text = request.Message.Parts.FirstOrDefault()?.Text ?? "";
    return new SendMessageResponse
    {
        Message = new Message
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Role = Role.Agent,
            Parts = [Part.FromText($"Echo: {text}")]
        }
    };
};

var agentCard = new AgentCard
{
    Name = "Echo Agent",
    Description = "Echoes messages back to the user",
    Version = "1.0.0",
    SupportedInterfaces = [new AgentInterface
    {
        Url = "http://localhost:5000/echo",
        ProtocolBinding = "JSONRPC",
        ProtocolVersion = "1.0"
    }],
    DefaultInputModes = ["text/plain"],
    DefaultOutputModes = ["text/plain"],
    Capabilities = new AgentCapabilities { Streaming = false },
    Skills = [new AgentSkill
    {
        Id = "echo",
        Name = "Echo",
        Description = "Echoes back user messages",
        Tags = ["echo"]
    }],
};

app.MapA2A(taskManager, "/echo");           // A2A protocol endpoint
app.MapWellKnownAgentCard(agentCard);        // /.well-known/agent-card.json
app.Run();
```

### 3.2 Minimal A2A Client

```csharp
using A2A;

// 1. Discover agent via well-known endpoint
var cardResolver = new A2ACardResolver(new Uri("http://localhost:5000/"));
var agentCard = await cardResolver.GetAgentCardAsync();

// 2. Create client using agent's resolved endpoint
var client = new A2AClient(new Uri(agentCard.SupportedInterfaces[0].Url));

// 3. Send message
var response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = new Message
    {
        MessageId = Guid.NewGuid().ToString("N"),
        Role = Role.User,
        Parts = [Part.FromText("Hello!")]
    }
});

// 4. Handle response (either immediate message or task)
switch (response.PayloadCase)
{
    case SendMessageResponseCase.Message:
        Console.WriteLine(response.Message!.Parts[0].Text);
        break;
    case SendMessageResponseCase.Task:
        Console.WriteLine($"Task created: {response.Task!.Id}");
        break;
}
```
[^350^]

### 3.3 Agent Card Creation in .NET

The Agent Card is a JSON metadata document describing an agent's capabilities. In .NET, it maps to the `AgentCard` class: [^432^] [^79^]

```csharp
var agentCard = new AgentCard
{
    Name = "InvoiceAgent",
    Description = "Handles requests relating to invoices.",
    Version = "1.0.0",
    SupportedInterfaces = [
        new AgentInterface
        {
            Url = "https://myagent.example.com/a2a",
            ProtocolBinding = ProtocolBindingNames.HttpJson,  // "HTTP+JSON"
            ProtocolVersion = "1.0"
        }
    ],
    DefaultInputModes = ["text/plain", "application/json"],
    DefaultOutputModes = ["text/plain", "application/json"],
    Capabilities = new AgentCapabilities
    {
        Streaming = true,
        PushNotifications = false,
        StateTransitionHistory = false
    },
    Skills = [
        new AgentSkill
        {
            Id = "invoice-query",
            Name = "InvoiceQuery",
            Description = "Handles requests relating to invoices.",
            Tags = ["invoice", "semantic-kernel"],
            Examples = ["List the latest invoices for Contoso."]
        }
    ],
    // Optional: security scheme declaration (OpenAPI-compatible)
    SecuritySchemes = new Dictionary<string, SecurityScheme>
    {
        ["apiKey"] = new ApiKeySecurityScheme
        {
            ApiKeyName = "X-API-Key",
            In = ApiKeyLocation.Header
        }
    }
};
```

**The Agent Card JSON served at `/.well-known/agent-card.json`:** [^79^] [^366^]

```json
{
  "name": "InvoiceAgent",
  "description": "Handles requests relating to invoices.",
  "version": "1.0.0",
  "supportedInterfaces": [
    {
      "url": "https://myagent.example.com/a2a",
      "protocolBinding": "HTTP+JSON",
      "protocolVersion": "1.0"
    }
  ],
  "capabilities": {
    "streaming": true,
    "pushNotifications": false
  },
  "skills": [
    {
      "id": "invoice-query",
      "name": "InvoiceQuery",
      "description": "Handles requests relating to invoices.",
      "tags": ["invoice"],
      "examples": ["List the latest invoices for Contoso."]
    }
  ],
  "defaultInputModes": ["text/plain"],
  "defaultOutputModes": ["text/plain"]
}
```

**Required fields:** `name`, `description`, `version`, `skills` (at least one), `supportedInterfaces`, `defaultInputModes`, `defaultOutputModes`. [^365^]

### 3.4 Task Lifecycle Implementation

#### Task States

The `TaskState` enum in .NET: [^402^] [^399^]

```csharp
public enum TaskState
{
    Submitted,      // Initial submission
    Working,        // Agent is processing
    InputRequired,  // Agent needs more input from client
    Completed,      // Successfully finished
    Canceled,       // Intentionally stopped
    Failed,         // Unrecoverable error
    Rejected,       // Agent refused the task
    AuthRequired    // Authentication needed
}
```

**State transition flow:** [^346^] [^399^]

```
submitted -> working -> completed
                    -> failed
                    -> canceled
                    -> input-required -> working -> completed
                    -> rejected
```

#### Server-Side Task Lifecycle Code

Implementing task lifecycle with `ITaskManager`: [^351^] [^346^]

```csharp
public class MyAgentLogic : IAgentLogicInvoker
{
    private readonly ITaskManager _taskManager;

    public MyAgentLogic(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public async Task ProcessTaskAsync(
        Common.Models.Task task, 
        Message triggeringMessage, 
        CancellationToken cancellationToken)
    {
        // 1. Update status to Working
        await _taskManager.UpdateTaskStatusAsync(
            task.Id, TaskState.Working, null, cancellationToken);

        try
        {
            // 2. Perform agent work
            var userText = triggeringMessage.Parts
                .OfType<TextPart>().FirstOrDefault()?.Text;
            var resultText = await DoWork(userText);

            // 3. Create artifact with results
            var resultArtifact = new Artifact
            {
                Parts = new List<Part> { new TextPart(resultText) }
            };

            // 4. Add artifacts
            await _taskManager.AddArtifactAsync(
                task.Id, resultArtifact, cancellationToken);

            // 5. Mark as Completed
            await _taskManager.UpdateTaskStatusAsync(
                task.Id, TaskState.Completed, null, cancellationToken);
        }
        catch (Exception ex)
        {
            // Mark as Failed on error
            await _taskManager.UpdateTaskStatusAsync(
                task.Id, TaskState.Failed, 
                new Message { Parts = [new TextPart(ex.Message)] }, 
                cancellationToken);
        }
    }
}
```

#### Client-Side Task Polling

```csharp
// Send message that creates a task
var response = await client.SendMessageAsync(new SendMessageRequest
{
    Message = userMessage
});

if (response.Task != null)
{
    var taskId = response.Task.Id;
    
    // Poll until terminal state
    AgentTask task;
    do
    {
        await Task.Delay(1000);
        task = await client.GetTaskAsync(new GetTaskRequest { Id = taskId });
        Console.WriteLine($"State: {task.Status.State}");
    } 
    while (task.Status.State != TaskState.Completed 
           && task.Status.State != TaskState.Failed);
}
```

### 3.5 SSE Streaming for Long-Running Tasks

#### Client-Side Streaming Consumption

The SDK provides `SendStreamingMessageAsync` that returns an `IAsyncEnumerable<StreamResponse>`: [^404^] [^513^]

```csharp
A2AClient client = new A2AClient(new Uri(agentCard.Url));

Message userMessage = new()
{
    Role = Role.User,
    MessageId = Guid.NewGuid().ToString("N"),
    Parts = [Part.FromText("Generate a detailed research report")]
};

await foreach (StreamResponse streamEvent in 
    client.SendStreamingMessageAsync(new SendMessageRequest { Message = userMessage }))
{
    // Task creation event
    if (streamEvent.Task is { } task)
    {
        Console.WriteLine($"Task: {task.Id} (state: {task.Status.State})");
    }

    // Status update (progress)
    if (streamEvent.StatusUpdate is { } statusUpdate)
    {
        Console.WriteLine($"Status: {statusUpdate.Status.State}");
        if (statusUpdate.Status.Message?.Parts[0] is TextPart tp)
        {
            Console.WriteLine($"  Message: {tp.Text}");
        }
    }

    // Artifact chunk (partial results)
    if (streamEvent.ArtifactUpdate is { } artifactUpdate)
    {
        var text = artifactUpdate.Artifact.Parts[0].Text ?? "(non-text)";
        Console.WriteLine($"Chunk (append:{artifactUpdate.Append}, " +
            $"lastChunk:{artifactUpdate.LastChunk}): {text[..Math.Min(80, text.Length)]}");
    }
}
```
[^513^]

#### How SSE Streaming Works Under the Hood

The A2A server sends SSE events over an open HTTP connection. Each event contains a JSON-RPC response: [^120^]

```
HTTP/1.1 200 OK
Content-Type: text/event-stream

// Progress update
event: message
data: {
  "jsonrpc": "2.0",
  "id": "client-req-1",
  "result": {
    "id": "task-podcast-123",
    "status": {
      "state": "working",
      "message": {
        "role": "agent",
        "parts": [{"type": "text", "text": "Drafting introduction..."}]
      }
    },
    "final": false
  }
}

// Completion event
event: message
data: {
  "jsonrpc": "2.0",
  "id": "client-req-1",
  "result": {
    "id": "task-podcast-123",
    "status": {
      "state": "completed",
      "message": {
        "role": "agent",
        "parts": [{"type": "text", "text": "Final script generated."}]
      }
    },
    "final": true
  }
}
```

### 3.6 Semantic Kernel A2A Integration

Microsoft's Semantic Kernel provides built-in A2A support. The official sample is at `dotnet/samples/Demos/A2AClientServer` in the semantic-kernel repo. [^396^]

#### A2A Server with Semantic Kernel

**HostAgentFactory.cs** - Creating agents and their Agent Cards: [^432^]

```csharp
using A2A;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.A2A;

namespace A2AServer;

internal static class HostAgentFactory
{
    internal static async Task<A2AHostAgent> CreateChatCompletionHostAgentAsync(
        string agentType, string modelId, string apiKey, 
        string name, string instructions,
        IEnumerable<KernelPlugin>? plugins = null)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId, apiKey);
        
        if (plugins is not null)
        {
            foreach (var plugin in plugins)
                builder.Plugins.Add(plugin);
        }

        var kernel = builder.Build();
        var agent = new ChatCompletionAgent()
        {
            Kernel = kernel,
            Name = name,
            Instructions = instructions,
            Arguments = new KernelArguments(new PromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            }),
        };

        AgentCard agentCard = GetAgentCardForType(agentType);
        return new A2AHostAgent(agent, agentCard);
    }

    private static AgentCard GetInvoiceAgentCard()
    {
        var capabilities = new AgentCapabilities()
        {
            Streaming = false,
            PushNotifications = false,
        };
        var skill = new AgentSkill()
        {
            Id = "id_invoice_agent",
            Name = "InvoiceQuery",
            Description = "Handles requests relating to invoices.",
            Tags = ["invoice", "semantic-kernel"],
            Examples = ["List the latest invoices for Contoso."],
        };
        return new AgentCard()
        {
            Name = "InvoiceAgent",
            Description = "Handles requests relating to invoices.",
            Version = "1.0.0",
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Capabilities = capabilities,
            Skills = [skill],
        };
    }
}
```

**Server Program.cs** - Exposing SK agent via A2A: [^485^]

```csharp
using A2A;
using A2A.AspNetCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.A2A;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient().AddLogging();
var app = builder.Build();

// Create the Semantic Kernel agent wrapped as A2A host
A2AHostAgent? hostAgent = await HostAgentFactory.CreateChatCompletionHostAgentAsync(
    agentType: "INVOICE",
    modelId: "gpt-4o-mini",
    apiKey: configuration["A2AServer:ApiKey"]!,
    name: "InvoiceAgent",
    instructions: "You specialize in handling queries related to invoices.",
    plugins: [KernelPluginFactory.CreateFromType<InvoiceQueryPlugin>()]);

// Map A2A endpoints - TaskManager handles all protocol details
app.MapA2A(hostAgent!.TaskManager!, "/");
app.MapWellKnownAgentCard(hostAgent!.TaskManager!, "/");
await app.RunAsync();
```

#### A2A Client with Semantic Kernel

**HostClientAgent.cs** - Orchestrating multiple A2A agents as SK plugins: [^484^]

```csharp
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Agents.A2A;

internal sealed class HostClientAgent
{
    internal async Task InitializeAgentAsync(
        string modelId, string apiKey, string[] agentUrls)
    {
        // 1. Connect to remote A2A agents and discover capabilities
        var createAgentTasks = agentUrls.Select(url => this.CreateAgentAsync(url));
        var agents = await Task.WhenAll(createAgentTasks);
        
        // 2. Convert A2A agents to Semantic Kernel functions
        var agentFunctions = agents
            .Select(a => AgentKernelFunctionFactory.CreateFromAgent(a))
            .ToList();
        var agentPlugin = KernelPluginFactory
            .CreateFromFunctions("AgentPlugin", agentFunctions);

        // 3. Build SK kernel with A2A agents as plugins
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId, apiKey);
        builder.Plugins.Add(agentPlugin);
        var kernel = builder.Build();

        // 4. Create orchestrator agent that can invoke A2A agents
        this.Agent = new ChatCompletionAgent()
        {
            Kernel = kernel,
            Name = "HostClient",
            Instructions = """
                You specialize in handling queries for users 
                and using your tools to provide answers.
                """,
            Arguments = new KernelArguments(new PromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            }),
        };
    }

    private async Task<A2AAgent> CreateAgentAsync(string agentUri)
    {
        var url = new Uri(agentUri);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        
        var client = new A2AClient(url, httpClient);
        var cardResolver = new A2ACardResolver(url, httpClient);
        var agentCard = await cardResolver.GetAgentCardAsync();
        
        return new A2AAgent(client, agentCard!);
    }
}
```

### 3.7 Microsoft Agent Framework A2A Hosting (v1)

The newer Microsoft Agent Framework provides a refined hosting model for A2A v1: [^345^] [^362^]

```csharp
using A2A;
using A2A.AspNetCore;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

// 1. Create and register the agent
builder.Services.AddKeyedSingleton<AIAgent>("weather-agent", (sp, _) =>
{
    return new AIProjectClient(
            new Uri("https://your-project.azure.com"), 
            new DefaultAzureCredential())
        .AsAIAgent(
            model: "gpt-4o-mini",
            instructions: "You are a helpful weather assistant.",
            name: "weather-agent");
});

// 2. Register the A2A server for the agent
builder.AddA2AServer("weather-agent");

var app = builder.Build();

// 3. Map A2A protocol endpoints
app.MapA2AHttpJson("weather-agent", "/a2a/weather-agent");
app.MapA2AJsonRpc("weather-agent", "/a2a/weather-agent");  // Optional dual binding

// 4. Serve agent card for discovery
app.MapWellKnownAgentCard(new AgentCard
{
    Name = "WeatherAgent",
    Description = "A helpful weather assistant.",
    SupportedInterfaces = [
        new AgentInterface
        {
            Url = "https://your-host/a2a/weather-agent",
            ProtocolBinding = ProtocolBindingNames.HttpJson,
            ProtocolVersion = "1.0",
        }
    ]
});

app.Run();
```

---

## 4. Configuration & Setup

### 4.1 Required NuGet Packages

```bash
# Open-source A2A SDK (recommended for direct protocol access)
dotnet add package A2A --prerelease
dotnet add package A2A.AspNetCore --prerelease

# Microsoft Agent Framework hosting (recommended for MS ecosystem)
dotnet add package Microsoft.Agents.AI.Hosting.A2A.AspNetCore --prerelease
dotnet add package Microsoft.Agents.AI.Hosting.A2A --prerelease

# Semantic Kernel integration (if using SK agents)
dotnet add package Microsoft.SemanticKernel.Agents

# For Azure AI Foundry integration
dotnet add package Azure.AI.Projects --prerelease
dotnet add package Azure.Identity
dotnet add package Microsoft.Agents.AI.Foundry --prerelease
```
[^344^] [^362^] [^364^]

### 4.2 Project Structure

Recommended structure for a multi-agent system: [^346^]

```
MyAgentSystem/
  AgentServer/               # ASP.NET Core Web API
    Program.cs               # Host agent, map A2A endpoints
    AgentFactory.cs          # Create agents and agent cards
    Plugins/
      MyPlugin.cs            # Semantic Kernel plugins
  AgentClient/               # Console or Web app
    Program.cs               # Discover agents, send messages
    AgentOrchestrator.cs     # Multi-agent coordination
```

### 4.3 A2A Service Registration in DI

For the Microsoft Agent Framework approach: [^344^] [^362^]

```csharp
// Register agents as keyed singletons
builder.Services.AddKeyedSingleton<AIAgent>("agent1", (sp, _) => ...);
builder.Services.AddKeyedSingleton<AIAgent>("agent2", (sp, _) => ...);

// Register A2A servers
builder.AddA2AServer("agent1");
builder.AddA2AServer("agent2");

// Map endpoints
app.MapA2AHttpJson("agent1", "/a2a/agent1");
app.MapA2AHttpJson("agent2", "/a2a/agent2");
```

---

## 5. Integration Patterns

### 5.1 Agent Discovery Pattern

Discovery follows the well-known URI standard: [^79^] [^349^]

```csharp
// Client discovers agent capabilities
var resolver = new A2ACardResolver(new Uri("https://agent.example.com"));
var card = await resolver.GetAgentCardAsync();

// Inspect capabilities before calling
Console.WriteLine($"Agent: {card.Name}");
Console.WriteLine($"Supports streaming: {card.Capabilities.Streaming}");
foreach (var skill in card.Skills)
{
    Console.WriteLine($"  Skill: {skill.Name} - {skill.Description}");
}

// Use the discovered endpoint
var endpoint = card.SupportedInterfaces[0].Url;
var client = new A2AClient(new Uri(endpoint));
```

The Agent Card is served at `https://agent.example.com/.well-known/agent-card.json`. [^79^] [^366^]

### 5.2 A2A + Semantic Kernel Plugin Pattern

A2A agents can be dynamically converted to Semantic Kernel functions: [^484^] [^82^]

```csharp
// Each A2A agent becomes a KernelFunction
var agentFunctions = agents
    .Select(agent => AgentKernelFunctionFactory.CreateFromAgent(agent))
    .ToList();

// Bundle into a plugin
var plugin = KernelPluginFactory.CreateFromFunctions("A2AAgents", agentFunctions);

// Add to kernel - SK can now auto-invoke A2A agents
kernelBuilder.Plugins.Add(plugin);
```

### 5.3 Multi-Agent Server Pattern

Hosting multiple agents in a single ASP.NET Core app: [^345^] [^362^]

```csharp
// Register multiple agents
builder.Services.AddKeyedSingleton<AIAgent>("weather", ...);
builder.Services.AddKeyedSingleton<AIAgent>("scientist", ...);

builder.AddA2AServer("weather");
builder.AddA2AServer("scientist");

// Each gets its own endpoint
app.MapA2AHttpJson("weather", "/a2a/weather");
app.MapA2AHttpJson("scientist", "/a2a/scientist");
```

### 5.4 JSON-RPC 2.0 Message Formats

All JSON-RPC requests follow the standard format: [^78^] [^352^]

**Send Message Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "message/send",
  "params": {
    "message": {
      "role": "user",
      "parts": [{ "type": "text", "text": "tell me a joke" }],
      "messageId": "9229e770-767c-417b-a0b0-f0741243c589"
    },
    "metadata": {}
  }
}
```

**Successful Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "messageId": "363422be-b0f9-4692-a24d-278670e7c7f1",
    "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4",
    "parts": [
      {
        "type": "text",
        "text": "Why did the chicken cross the road?"
      }
    ],
    "kind": "message",
    "metadata": {}
  }
}
```

**Task Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "id": "task-uuid",
    "contextId": "context-uuid",
    "status": { "state": "TASK_STATE_COMPLETED" },
    "artifacts": [
      {
        "artifactId": "art-1",
        "name": "result.json",
        "parts": [{ "type": "data", "data": { "key": "value" } }]
      }
    ]
  }
}
```

**Error Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32602,
    "message": "Invalid parameters",
    "data": [
      {
        "@type": "type.googleapis.com/google.rpc.BadRequest",
        "fieldViolations": [
          { "field": "message.parts", "description": "At least one part is required" }
        ]
      }
    ]
  }
}
```
[^78^] [^352^]

### 5.5 HTTP+JSON REST Endpoints (A2A v1)

The newer HTTP+JSON binding uses RESTful URL patterns: [^79^]

| Operation | Method | Path | Description |
|-----------|--------|------|-------------|
| Send Message | POST | `/message:send` | Send a message |
| Stream Message | POST | `/message:stream` | Send with SSE streaming |
| Get Task | GET | `/tasks/{id}` | Get task status |
| List Tasks | GET | `/tasks` | List tasks with query params |
| Cancel Task | POST | `/tasks/{id}:cancel` | Cancel a task |
| Subscribe | POST | `/tasks/{id}:subscribe` | Subscribe to updates via SSE |
| Get Agent Card | GET | `/.well-known/agent-card.json` | Discovery |
| Extended Card | GET | `/extendedAgentCard` | Authenticated card |

---

## 6. Authentication & Security

### 6.1 Supported Authentication Schemes

A2A delegates authentication to standard web security mechanisms. The Agent Card declares supported schemes using OpenAPI-compatible formats: [^75^] [^81^] [^10^]

| Scheme | Use Case | Implementation |
|--------|----------|---------------|
| **API Key** | Simple token auth | `X-API-Key` header |
| **OAuth 2.0** | User-delegated access | Bearer token in `Authorization` header |
| **OpenID Connect** | Enterprise SSO | OIDC Discovery flow |
| **mTLS** | Service-to-service | Mutual TLS certificates |
| **None** | Public/internal endpoints | No auth required |

### 6.2 Declaring Authentication in Agent Card

```csharp
var agentCard = new AgentCard
{
    // ... other fields ...
    SecuritySchemes = new Dictionary<string, SecurityScheme>
    {
        ["apiKey"] = new ApiKeySecurityScheme
        {
            ApiKeyName = "X-API-Key",
            In = ApiKeyLocation.Header,
            Description = "API key for authentication"
        },
        ["oauth2"] = new OAuth2SecurityScheme
        {
            Flows = new OAuthFlows
            {
                ClientCredentials = new OAuthFlow
                {
                    TokenUrl = "https://auth.example.com/token",
                    Scopes = new Dictionary<string, string>
                    {
                        ["agents:invoke"] = "Invoke agent"
                    }
                }
            }
        }
    },
    Security = new List<Dictionary<string, List<string>>>
    {
        new() { ["apiKey"] = new List<string>() },
        new() { ["oauth2"] = new List<string> { "agents:invoke" } }
    }
};
```
[^81^] [^79^]

### 6.3 Client-Side Authentication

The A2A SDK itself is auth-agnostic; you pass a pre-configured `HttpClient`: [^81^] [^398^]

```csharp
// API Key authentication
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("X-API-Key", "your-api-key");
var client = new A2AClient(new Uri("https://agent.example.com"), httpClient);

// OAuth 2.0 Bearer token
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Authorization = 
    new AuthenticationHeaderValue("Bearer", accessToken);
var client = new A2AClient(new Uri("https://agent.example.com"), httpClient);
```

### 6.4 Server-Side Authentication

Use standard ASP.NET Core middleware: [^81^] [^77^]

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* configure */ });

var app = builder.Build();

// Apply auth middleware before A2A endpoints
app.UseAuthentication();
app.UseAuthorization();

app.MapA2A(taskManager, "/agent")
   .RequireAuthorization();  // Require auth for A2A endpoint

app.MapWellKnownAgentCard(agentCard);  // Typically public
```

---

## 7. Limitations & Gotchas

### 7.1 SDK Status
- **A2A .NET SDK is in preview** - both the open-source and Microsoft packages are pre-release [^348^] [^349^]
- **A2A protocol v1.0 is stable** but SDK implementations are still evolving [^345^]
- Breaking changes occurred between v0.3 and v1 - see migration guide [^344^]

### 7.2 Known Limitations

| Issue | Detail |
|-------|--------|
| Auth is DIY | SDK handles Agent Card serialization but NOT credential validation - you must wire up ASP.NET Core auth middleware [^81^] |
| Background responses | Not yet supported for A2A-hosted agents in MS Agent Framework [^362^] |
| Streaming reliability | Push notifications for disconnected clients need webhook infrastructure [^77^] |
| Single well-known card | Only one Agent Card can be served per host via `/.well-known/agent-card.json` - other agents need direct URLs [^344^] |
| No built-in authorization | The protocol does not define authorization - you must implement skill-level access control [^75^] |

### 7.3 Common Gotchas

1. **Protocol selection**: In v1, HTTP+JSON is the default preferred binding, NOT JSON-RPC. Set `A2AClientOptions.PreferredBindings = [ProtocolBindingNames.JsonRpc]` if you need the old behavior. [^344^]

2. **Default value serialization**: Empty arrays and default values are omitted from JSON during canonicalization for Agent Card signing. [^79^]

3. **Agent Card does NOT contain secrets** - it only *declares* auth requirements. Actual credentials are exchanged out-of-band. [^75^]

4. **Message Parts must have at least one item** - sending an empty parts array will result in error code `-32602` (Invalid parameters). [^78^]

5. **Content-Type headers**: JSON-RPC uses `application/json`; HTTP+JSON binding uses `application/a2a+json`. [^79^]

---

## 8. Recommendations for Prototype

### 8.1 Technology Stack

| Component | Recommendation | Rationale |
|-----------|---------------|-----------|
| SDK | `A2A` + `A2A.AspNetCore` packages | Cleanest, most documented, open-source |
| Hosting | ASP.NET Core Minimal APIs | `MapA2A()` handles all protocol boilerplate |
| AI Framework | Semantic Kernel (optional) | If you need LLM orchestration; A2A works standalone |
| Auth | API Key for dev, OAuth 2.0 for prod | Start simple, upgrade to enterprise auth |
| Storage | `InMemoryTaskStore` for dev, custom `ITaskStore` for prod | Easy to swap implementations |

### 8.2 Implementation Order

1. **Phase 1 - Basic Echo Agent**: Implement a minimal A2A server that echoes messages back (verify protocol compliance)
2. **Phase 2 - Agent Card**: Add a well-formed Agent Card with skills, capabilities, and auth declarations
3. **Phase 3 - Client Discovery**: Build a client that discovers the agent via `A2ACardResolver` and sends messages
4. **Phase 4 - Task Lifecycle**: Implement proper `submitted -> working -> completed` state transitions
5. **Phase 5 - Streaming**: Add SSE streaming for long-running tasks using `SendStreamingMessageAsync`
6. **Phase 6 - Semantic Kernel Integration**: Wrap your agent in `A2AHostAgent` and expose via SK

### 8.3 Minimal Viable Prototype

```csharp
// Server (Program.cs) - ~30 lines
using A2A;
using A2A.AspNetCore;
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var tm = new TaskManager(new InMemoryTaskStore());
tm.OnSendMessage = async (req, ct) => new SendMessageResponse {
    Message = new Message {
        Role = Role.Agent,
        Parts = [Part.FromText($"Echo: {req.Message.Parts[0].Text}")]
    }};
app.MapA2A(tm, "/");
app.MapWellKnownAgentCard(new AgentCard {
    Name = "EchoAgent", Version = "1.0.0",
    Skills = [new AgentSkill { Id = "echo", Name = "Echo" }]
});
app.Run();

// Client (Program.cs) - ~15 lines  
using A2A;
var resolver = new A2ACardResolver(new Uri("http://localhost:5000"));
var card = await resolver.GetAgentCardAsync();
var client = new A2AClient(new Uri(card.Url));
var resp = await client.SendMessageAsync(new SendMessageRequest {
    Message = new Message { Role = Role.User, Parts = [Part.FromText("Hello A2A!")] }
});
Console.WriteLine(resp.Message?.Parts[0].Text);
```

---

## 9. Sources & References

| # | Source | URL | Authority |
|---|--------|-----|-----------|
| [^78^] | A2A Protocol Specification (GitHub) | https://github.com/a2aproject/A2A/blob/main/docs/specification.md | Official (A2A Project) |
| [^79^] | A2A Protocol v1.0 Spec | https://a2a-protocol.org/latest/specification/ | Official (A2A Protocol) |
| [^350^] | a2a-dotnet (GitHub) | https://github.com/a2aproject/a2a-dotnet | Official (A2A Project) |
| [^351^] | A2Adotnet Community SDK | https://github.com/azixaka/a2adotnet | Community |
| [^344^] | A2A SDK v1 Migration Guide | https://learn.microsoft.com/en-us/agent-framework/migration-guide/agent-to-agent-sdk-v1 | Microsoft Learn |
| [^345^] | A2A v1 in Microsoft Agent Framework | https://devblogs.microsoft.com/agent-framework/a2a-v1-is-here-cross-platform-agent-communication-in-microsoft-agent-framework-for-net/ | Microsoft DevBlog |
| [^362^] | A2A Hosting (Microsoft Learn) | https://learn.microsoft.com/en-us/agent-framework/hosting/agent-to-agent | Microsoft Learn |
| [^363^] | A2A.AspNetCore NuGet Package | https://www.nuget.org/packages/A2A.AspNetCore/ | NuGet |
| [^396^] | Semantic Kernel A2A Sample (GitHub) | https://github.com/microsoft/semantic-kernel/tree/main/dotnet/samples/Demos/A2AClientServer | Microsoft GitHub |
| [^346^] | Implementing A2A Protocol in .NET | https://techcommunity.microsoft.com/blog/azuredevcommunityblog/implementing-a2a-protocol-in-net-a-practical-guide/4480232 | Microsoft TechCommunity |
| [^404^] | Building AI Agents with A2A .NET SDK | https://devblogs.microsoft.com/foundry/building-ai-agents-a2a-dotnet-sdk/ | Microsoft DevBlog |
| [^348^] | Microsoft Releases A2A .NET SDK | https://www.infoq.com/news/2025/08/a2a-dotnet-sdk/ | InfoQ |
| [^349^] | Getting Started with A2A in .NET | https://www.infoworld.com/article/4035247/getting-started-with-a2a-in-net.html | InfoWorld |
| [^75^] | A2A Protocol Security | https://securew2.com/blog/a2a-protocol-security | SecureW2 |
| [^81^] | A2A Protocol Security Guide | https://live.paloaltonetworks.com/t5/community-blogs/safeguarding-ai-agents-an-in-depth-look-at-a2a-protocol-risks/ba-p/1235996 | Palo Alto Networks |
| [^399^] | A2A Task Concept | https://agent2agent.info/docs/concepts/task/ | A2A Community |
| [^120^] | A2A Deep Dive: Real-Time Updates | https://medium.com/google-cloud/a2a-deep-dive-getting-real-time-updates-from-ai-agents-a28d60317332 | Google Cloud Blog |
| [^505^] | A2A Message and Part Types | https://trickle.so/blog/how-google-a2a-protocol-actually-works | Trickle Blog |
| [^10^] | What is A2A Protocol (IBM) | https://www.ibm.com/think/topics/agent2agent-protocol | IBM |
| [^82^] | A2A + Semantic Kernel + Azure AI Foundry | https://medium.com/data-science-collective/step-by-step-guide-to-building-multi-agent-systems-with-a2a-azure-ai-foundry-and-semantic-kernel-d996168b2a05 | Medium |
| [^402^] | A2A .NET SDK Documentation | https://a2aprotocol.ai/blog/a2a-dotnet-sdk | A2A Protocol |
