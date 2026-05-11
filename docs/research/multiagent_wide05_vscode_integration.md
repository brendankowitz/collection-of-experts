## Facet: VS Code Copilot Chat Integration & IDE Agent Patterns

### Key Findings

- **VS Code Chat Participant API** allows extensions to register @-mentionable domain experts that receive user prompts and orchestrate responses end-to-end. Registration uses `vscode.chat.createChatParticipant(id, handler)` in extension code and the `chatParticipants` contribution point in `package.json` [^19^].

- **Three types of tools** are available in VS Code's agent mode: built-in tools (contributed by VS Code), MCP tools (from MCP servers), and extension-contributed tools (registered via `vscode.lm.registerTool`) [^268^].

- **Participant detection (auto-routing)** works via the `disambiguation` property in `package.json`, where developers specify detection categories with descriptions and example questions. VS Code uses these to automatically route user prompts to the appropriate participant without explicit @-mentioning [^19^].

- **Response streaming** is handled through `ChatResponseStream` passed to the request handler, supporting `stream.markdown()`, `stream.progress()`, `stream.filetree()`, `stream.reference()`, `stream.anchor()`, and `stream.button()` for rich, interactive responses [^19^] [^270^].

- **MCP server support** is comprehensive: VS Code implements the full MCP specification, supporting stdio, HTTP, and SSE transports; tools, prompts, resources, elicitation, sampling, authentication, and MCP Apps (interactive UI components) [^264^].

- **Two integration paths exist**: VS Code Extensions (full VS Code API access, distributed via Marketplace) and GitHub Apps (cross-platform across github.com/VS/VS Code, but no VS Code API access) [^19^] [^305^].

- **The `@vscode/chat-extension-utils` library** simplifies chat participant development by handling the LLM tool-calling loop, prompt crafting (history, references, tool calls), and response streaming [^271^].

- **Agent mode became generally available** in VS Code Stable in March 2025 (v1.99), with MCP support enabling external tool integration [^282^] [^287^].

- **A2A protocol is not natively integrated** into VS Code's APIs, but chat participants can communicate with external A2A agents via standard HTTP requests within their request handlers, since participants have full access to Node.js/VS Code APIs [^341^].

- **Distribution guidelines** require adherence to Microsoft AI tools and practices guidelines and GitHub Copilot extensibility acceptable development and use policy. Extensions should NOT introduce a dependency on GitHub Copilot in the manifest if they contribute non-chat functionality [^19^] [^279^].

---

### Major Players & Sources

- **Microsoft / VS Code Team**: Authors of the Chat Participant API, Language Model API, MCP integration, and agent mode. Official docs at `code.visualstudio.com/api/extension-guides/ai/*` [^19^] [^264^] [^265^] [^266^] [^281^].

- **GitHub**: Provides Copilot Chat (the host for chat participants), the underlying LLMs (GPT-4o, Claude 3.5 Sonnet), and the GitHub Apps alternative extension path [^305^].

- **Stripe**: Early adopter of Chat Participant API with `@stripe` participant for payment integration assistance [^305^].

- **MongoDB**: Built `@mongodb` chat participant for query generation and schema insights [^305^].

- **Microsoft (Playwright)**: Provides official `@microsoft/mcp-server-playwright` MCP server for browser automation [^229^].

- **Ken Muse** (Blogger/Architect): Detailed analysis of VS Code AI extension API layers beyond MCP [^283^].

- **Google (A2A Protocol)**: Developed A2A (Agent-to-Agent) protocol for inter-agent communication; Linux Foundation project. Complements MCP by connecting agents to each other rather than agents to tools [^275^].

---

### Trends & Signals

- **Agent mode + MCP = extensible autonomous coding**: VS Code's agent mode (launched March 2025 Stable) uses MCP servers as its primary extensibility mechanism for external tools, making it the standard way to add capabilities [^287^] [^282^].

- **Chat participants evolving toward agents**: Advanced participants like `@workspace` act as autonomous agents that invoke multiple tools. VS Code encourages this pattern by providing `sendChatParticipantRequest` in `@vscode/chat-extension-utils` [^19^] [^271^].

- **MCP Apps enable interactive UI in chat**: Announced January 2026, MCP Apps let tools return interactive UI components (dashboards, forms, visualizations) rendered inline in chat conversations [^267^].

- **Participant auto-routing reduces friction**: The `disambiguation` API enables participants to be discovered and invoked without explicit @-mentions, improving UX significantly [^19^].

- **Consolidation around MCP as the tool standard**: VS Code draws a direct parallel between LSP (Language Server Protocol, 2016) and MCP -- they view MCP as fulfilling the same standardization vision for AI tools [^287^].

- **Extension tools ecosystem emerging**: Tools registered by one extension are accessible to others via `vscode.lm.tools`, enabling a composable tool ecosystem [^265^] [^340^].

---

### Controversies & Conflicting Claims

- **GitHub Apps vs VS Code Extensions trade-off**: GitHub Apps work across all Copilot surfaces but lack VS Code API access; VS Code Extensions have deep IDE integration but only work in VS Code. The documentation presents these as complementary, but developers must choose one path for a given capability [^19^] [^305^].

- **Agent mode trust and security**: All tool invocations (except read-only built-in tools) require user approval. This creates UX friction vs. fully autonomous agents. Dev Containers are recommended as a sandboxing approach [^287^].

- **Rate limiting and token constraints**: Language Model API requests are subject to quota limits. GPT-4o has a 64K token input limit for extensions. VS Code does not provide a sandbox/test LLM for integration testing [^266^].

- **A2A vs MCP relationship**: Google positions A2A as complementing MCP (A2A for agent-to-agent, MCP for agent-to-tool). There is no native A2A support in VS Code yet; integration would be manual via HTTP from a chat participant [^275^] [^283^].

- **Tool approval UX**: Some community members (e.g., Wallaby/Console Ninja teams) find the current tool registration/discovery model insufficiently flexible for their use cases, requesting deeper integration [^285^].

---

### Recommended Deep-Dive Areas

- **MCP Apps development**: Interactive UI components in chat represent a major UX evolution. Building MCP Apps requires understanding the `ui://` URI scheme, sandboxed iframes, and the `@modelcontextprotocol/ext-apps` SDK [^267^].

- **Prompt crafting with `@vscode/prompt-tsx`**: The TSX-based prompt composition library enables token-budget-aware prompt construction with priority-based content inclusion -- critical for sophisticated participants [^271^] [^283^].

- **Chat participant telemetry and success metrics**: VS Code recommends measuring `unhelpful_feedback_count / total_requests` via `onDidReceiveFeedback` events to iteratively improve participants [^19^].

- **Enterprise MCP server management**: Organizations can centrally manage MCP server access via GitHub policies, and servers support OAuth authentication for GitHub and Microsoft Entra [^229^] [^264^].

- **Agent mode prompt customization via `.github/agents/*.agent.md`**: GitHub renamed "chat modes" to "agents" and moved them to `.github/agents/`. These custom instructions shape agent behavior across sessions [^276^].

---

### Detailed Notes

#### 1. The Chat Participant API

**Registration**: Two-step process:
1. Declare in `package.json` under `contributes.chatParticipants`:
```json
"contributes": {
  "chatParticipants": [
    {
      "id": "chat-sample.cat",
      "name": "cat",
      "fullName": "Cat",
      "description": "Meow! What can I teach you?",
      "isSticky": true,
      "commands": [
        { "name": "teach", "description": "Pick at random a computer science concept..." }
      ]
    }
  ]
}
```
2. Register in extension code with `vscode.chat.createChatParticipant(id, handler)` [^19^].

**Request Handler Signature**:
```typescript
const handler: vscode.ChatRequestHandler = async (
  request: vscode.ChatRequest,
  context: vscode.ChatContext,
  stream: vscode.ChatResponseStream,
  token: vscode.CancellationToken
): Promise<ICatChatResult> => {
  // Handle the request
};
```

**Key Properties on `ChatRequest`**:
- `request.prompt`: User's text input
- `request.command`: Slash command invoked (e.g., "teach")
- `request.model`: The language model selected by the user (respect user choice!)
- `request.references`: Files/context attached to the prompt
- `request.toolReferences`: Tools explicitly referenced via `#` syntax [^338^]

**Chat Context History**: Access previous conversation turns via `context.history`, filtering by `ChatRequestTurn` and `ChatResponseTurn` to build multi-turn conversations [^270^].

#### 2. Language Model API for Participants

**Accessing Models**: Use `request.model` (from the ChatRequest) to respect user model selection, or select explicitly via `vscode.lm.selectChatModels({ vendor: 'copilot', family: 'gpt-4o' })` [^266^].

**Sending Requests**:
```typescript
const messages = [
  vscode.LanguageModelChatMessage.User('You are a helpful assistant...'),
  vscode.LanguageModelChatMessage.User(request.prompt)
];
const chatResponse = await request.model.sendRequest(messages, {}, token);
for await (const fragment of chatResponse.text) {
  stream.markdown(fragment);
}
```

**Supported Models**: `gpt-4o`, `gpt-4o-mini`, `o1`, `o1-mini`, `claude-3.5-sonnet`. GPT-4o recommended for quality; GPT-4o-mini for speed [^266^].

**Error Handling**: Use `LanguageModelError` to handle cases like model not existing, user consent not given, or quota exceeded [^266^].

#### 3. Tool Calling Within Chat Participants

**Two Approaches**:

1. **Using `@vscode/chat-extension-utils` (recommended for simplicity)**:
```typescript
const libResult = chatUtils.sendChatParticipantRequest(
  request, chatContext,
  {
    prompt: 'You are a cat! Answer as a cat.',
    responseStreamOptions: { stream, references: true, responseText: true },
    tools: vscode.lm.tools.filter(tool => tool.tags.includes('my-tag'))
  }, token);
return await libResult.result;
```
This handles the entire tool-calling loop, prompt crafting, and response streaming [^271^].

2. **Manual implementation (full control)**:
- Discover tools via `vscode.lm.tools`
- Pass tools in `sendRequest` options
- Handle `LanguageModelToolCallPart` responses
- Invoke tools via `vscode.lm.invokeTool(name, options, token)`
- Feed results back into subsequent LLM requests [^265^] [^338^]

**Tool Definition in package.json**:
```json
"contributes": {
  "languageModelTools": [
    {
      "name": "chat-tools-sample_tabCount",
      "tags": ["editors"],
      "toolReferenceName": "tabCount",
      "displayName": "Tab Count",
      "modelDescription": "The number of active tabs in a tab group.",
      "inputSchema": { "type": "object", ... },
      "when": "debugState == 'running'"
    }
  ]
}
```

#### 4. MCP Server Support in VS Code

**Capabilities Supported** [^264^]:
- Transports: stdio, Streamable HTTP, SSE (legacy)
- Features: Tools, Prompts, Resources, Elicitation, Sampling, Authentication (OAuth), Server instructions, Roots, MCP Apps
- VS Code first major editor with full MCP Apps support (Jan 2026)

**Adding MCP Servers**:
- Extensions view: search `@mcp`, click Install
- `.vscode/mcp.json` (workspace-level)
- User profile global config via `MCP: Open User Configuration`
- Auto-discovery from Claude Desktop
- Programmatic registration via `vscode.lm.registerMcpServerDefinitionProvider()`
- VS Code CLI: `--add-mcp` flag
- Dev Containers via `devcontainer.json`
- Installation URLs: `vscode:mcp/install?{json-config}` [^229^] [^264^]

**MCP Server Definition Provider API**:
```typescript
vscode.lm.registerMcpServerDefinitionProvider('myProvider', {
  onDidChangeMcpServerDefinitions: emitter.event,
  provideMcpServerDefinitions: async () => [
    new vscode.McpStdioServerDefinition({ label: 'myServer', command: 'node', args: ['server.js'], ... }),
    new vscode.McpHttpServerDefinition({ label: 'myRemoteServer', uri: 'http://localhost:3000', ... })
  ],
  resolveMcpServerDefinition: async (server) => { /* auth, setup */ return server; }
});
```

#### 5. External Service Communication (A2A/MCP)

**MCP Integration**: Direct and comprehensive -- MCP servers are first-class tools in VS Code's agent mode. Chat participants can leverage MCP tools through `vscode.lm.tools` and `sendChatParticipantRequest` [^264^] [^271^].

**A2A Integration**: No native A2A support in VS Code APIs. However, since chat participants run in the extension host with full Node.js/VS Code API access, they can make HTTP requests to A2A-compliant agents externally. The A2A protocol uses JSON-RPC 2.0 over HTTP with agent cards at `/.well-known/agent.json` [^275^] [^341^].

**Pattern**: A chat participant could:
1. Receive user prompt via ChatRequestHandler
2. Discover external A2A agents via their agent cards
3. Send A2A `agent/invoke` requests via `fetch()` or HTTP clients
4. Stream results back via `ChatResponseStream`

#### 6. Existing Chat Participant Examples

**Built-in Participants** [^306^]:
- `@workspace`: Workspace-wide code knowledge using GitHub knowledge graph, semantic search, local indexes
- `@terminal`: Terminal/shell command expertise
- `@vscode`: VS Code features, settings, extension APIs
- `@github`: GitHub repos, issues, PRs

**Extension-Contributed Participants** [^305^]:
- `@stripe`: Payment integration code generation and debugging
- `@mongodb`: Query generation, schema insights, performance analysis
- `@parallels`: VM operations via natural language
- `@pg` (PostgreSQL): Schema-aware SQL assistance
- `@websearch`: Live web search via Tavily or Bing [^337^]

#### 7. Participant Detection (Auto-Routing)

**The `disambiguation` Property**:
```json
"contributes": {
  "chatParticipants": [
    {
      "id": "my-ext.my-participant",
      "name": "tutor",
      "disambiguation": [
        {
          "category": "coding_help",
          "description": "The user wants to learn a programming concept or get coding help.",
          "examples": ["Teach me about recursion", "How do I use async/await?"]
        }
      ]
    }
  ]
}
```

VS Code uses these descriptions and examples to automatically route matching prompts to the participant without requiring an @-mention [^19^]. Detection can be specified at the participant level, per-command, or both.

#### 8. Response Streaming and Progress

**ChatResponseStream Methods** [^19^]:
- `stream.markdown(text)` -- Render markdown text
- `stream.progress(message)` -- Show progress message during long operations
- `stream.filetree(tree, baseLocation)` -- Render file tree preview
- `stream.reference(uri)` -- Add external URL or file reference
- `stream.anchor(location, title)` -- Inline reference to symbol/location
- `stream.button({ command, title })` -- Interactive button triggering VS Code command

**Progress Example**:
```typescript
stream.progress('Connecting to the database...');
// ... do work
stream.progress('Fetching results...');
```

**Follow-up Questions**: Provide via `followupProvider` on the participant:
```typescript
participant.followupProvider = {
  provideFollowups(result, context, token) {
    return [{ prompt: 'Tell me more', label: 'Learn more', command: 'help' }];
  }
};
```

#### 9. Packaging and Distribution

**Requirements** [^19^] [^279^]:
1. Read Microsoft AI tools and practices guidelines
2. Adhere to GitHub Copilot extensibility acceptable development and use policy
3. Use `vsce package` and `vsce publish` with a Personal Access Token
4. **Important**: Do NOT add `extensionDependencies` on GitHub Copilot if your extension has non-chat functionality
5. No special AI-specific packaging requirements beyond standard VS Code extension publishing

**Marketplace Listing**: Standard VS Code extension metadata (name, description, icon, README). The `chatParticipants` contribution is automatically surfaced in the extension's details.

#### 10. GitHub Copilot vs Standalone Integration

**VS Code Extension Path** (recommended for IDE integration):
- Full access to VS Code APIs (editor, debug, terminal, workspace)
- Distributed via VS Code Marketplace
- Works only in VS Code
- Can use `request.model` from Copilot's LLMs (requires Copilot subscription)
- Can contribute tools, participants, and MCP servers

**GitHub App Path** (recommended for cross-platform):
- Backed by a service, works across github.com, Visual Studio, VS Code
- NO access to VS Code APIs
- Distributed via GitHub Marketplace
- Requires joining Copilot Partner Program [^19^] [^305^]

**Key Insight**: Agent mode in VS Code IS essentially a chat participant registered with a special flag in `package.json`, using `vscode.lm.invokeTool` to invoke tools, as confirmed by the Copilot Chat extension's own CONTRIBUTING.md [^304^].

#### 11. Agent Mode Architecture

**Tool-Calling Flow** [^265^]:
1. User sends chat prompt
2. Copilot determines available tools (built-in + extensions + MCP servers)
3. LLM receives prompt + context + tool definitions
4. LLM may request tool invocations with generated parameters
5. Copilot invokes tools (with user confirmation for non-read-only tools)
6. Iterate until all tool requests resolved
7. Return final response to user

**Tool Types Priority** [^268^]:
- Built-in tools: No confirmation needed (read-only by default)
- Extension tools: Generic confirmation dialog (customizable via `prepareInvocation`)
- MCP tools: Subject to trust/approval workflow

**Explicit Tool References**: Users can force specific tools with `#toolname` syntax, e.g., `"What is Node.js latest? #web"` [^268^].

---

*Research compiled from 10+ independent searches across official VS Code documentation, Microsoft engineering blogs, GitHub repositories, and authoritative developer publications.*
