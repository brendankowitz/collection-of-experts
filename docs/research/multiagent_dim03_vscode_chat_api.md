# VS Code Chat Participant API - Deep Research

## Executive Summary

VS Code's Chat Participant API enables developers to build @-mentionable expert agents ("chat participants") that integrate directly into the GitHub Copilot Chat experience. The API is production-stable as of VS Code 1.90+ (May 2024) and consists of three layers: **Chat Participant API** (conversation orchestration), **Language Model API** (LLM access), and **Language Model Tool API** (tool registration). A helper library `@vscode/chat-extension-utils` dramatically simplifies the tool-calling loop. Participants are registered via `package.json` contributions, implemented as `ChatRequestHandler` functions, and can stream rich responses (markdown, progress, file trees, buttons, references) back to the user. [^19^] [^270^]

---

## 1. Technical Architecture & Components

### Three-Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    VS Code Chat UI                           │
│  (@mentions, slash commands, streaming responses)           │
└───────────────────────┬─────────────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────────────┐
│           Chat Participant API (vscode.chat.*)              │
│  - createChatParticipant(id, handler)                       │
│  - ChatRequestHandler(request, context, stream, token)      │
│  - ChatResponseStream (markdown, progress, buttons, etc.)   │
└───────────────────────┬─────────────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────────────┐
│          Language Model API (vscode.lm.*)                   │
│  - selectChatModels(selector)                               │
│  - LanguageModelChat.sendRequest(messages, options, token)  │
│  - LanguageModelChatMessage.User() / .Assistant()           │
└───────────────────────┬─────────────────────────────────────┘
                        │
┌───────────────────────▼─────────────────────────────────────┐
│         Language Model Tool API (vscode.lm.*)               │
│  - registerTool(name, toolInstance)                         │
│  - LanguageModelTool<T> interface                           │
│  - MCP server integration (registerMcpServerDefinitionProvider)
└─────────────────────────────────────────────────────────────┘
```

### Key API Namespaces

| Namespace | Purpose | Key Functions/Types |
|-----------|---------|-------------------|
| `vscode.chat` | Participant registration | `createChatParticipant()`, `ChatRequestHandler` |
| `vscode.lm` | Language model access & tools | `selectChatModels()`, `sendRequest()`, `registerTool()`, `tools` |
| `ChatResponseStream` | Response streaming to UI | `markdown()`, `progress()`, `button()`, `reference()`, `filetree()`, `anchor()` |
| `LanguageModelChatMessage` | Prompt construction | `User()`, `Assistant()` |
| `LanguageModelTool<T>` | Tool implementation | `prepareInvocation()`, `invoke()` |

---

## 2. package.json Contribution for Chat Participants

### 2.1 Basic Participant Registration

```json
{
  "name": "my-chat-extension",
  "displayName": "My Chat Extension",
  "version": "0.0.1",
  "publisher": "my-publisher",
  "engines": {
    "vscode": "^1.93.0"
  },
  "categories": ["AI", "Chat"],
  "activationEvents": [],
  "main": "./out/extension.js",
  "contributes": {
    "chatParticipants": [
      {
        "id": "chat-sample.cat",
        "name": "cat",
        "fullName": "Cat",
        "description": "Meow! What can I teach you?",
        "isSticky": true,
        "commands": [
          {
            "name": "teach",
            "description": "Pick at random a computer science concept then explain it"
          },
          {
            "name": "play",
            "description": "Do whatever you want, you are a cat after all"
          }
        ],
        "disambiguation": [
          {
            "category": "cat",
            "description": "The user wants to learn a specific computer science topic in an informal way.",
            "examples": [
              "Teach me C++ pointers using metaphors",
              "Explain to me what is a linked list in a simple way"
            ]
          }
        ]
      }
    ]
  }
}
```

### 2.2 Participant Properties Reference

| Property | Required | Description | Naming Guidelines |
|----------|----------|-------------|-------------------|
| `id` | Yes | Globally unique identifier | Use extension name as prefix: `chat-sample.cat` |
| `name` | Yes | @-mention name (lowercase) | Alphanumeric, underscores, hyphens only |
| `fullName` | No | Display name in response header | Title case: `"GitHub Copilot"` |
| `description` | No | Placeholder text in chat input | Short, sentence case, no punctuation |
| `isSticky` | No | Persist participant after response | `true` keeps @participant in input field |
| `commands` | No | Slash commands array | Each has `name` and `description` |
| `disambiguation` | No | Auto-detection categories | Array of `{category, description, examples[]}` |

### 2.3 Naming Conventions

- **`name`**: lowercase only (e.g., `cat`, `terminal`, `workspace`)
- **`fullName`**: Title case (e.g., `GitHub Copilot`, `VS Code`)
- **`description`**: Sentence case, no trailing punctuation
- **`id`**: `{publisher}.{participant-id}` format
- Some participant names are **reserved** — if used, VS Code shows the fully qualified name including extension ID [^19^]

### 2.4 Language Model Tool Registration (in same package.json)

```json
{
  "contributes": {
    "languageModelTools": [
      {
        "name": "chat-tools-sample_tabCount",
        "tags": ["editors", "chat-tools-sample"],
        "toolReferenceName": "tabCount",
        "displayName": "Tab Count",
        "modelDescription": "The number of active tabs in a tab group in VS Code.",
        "userDescription": "Count the number of active tabs in a tab group.",
        "canBeReferencedInPrompt": true,
        "icon": "$(files)",
        "inputSchema": {
          "type": "object",
          "properties": {
            "tabGroup": {
              "type": "number",
              "description": "The index of the tab group to check.",
              "default": 0
            }
          }
        },
        "when": "debugState == 'running'"
      }
    ]
  }
}
```

---

## 3. ChatRequestHandler - Full Implementation

### 3.1 Handler Signature

```typescript
import * as vscode from 'vscode';

interface IMyChatResult extends vscode.ChatResult {
  metadata: {
    command: string;
  };
}

const handler: vscode.ChatRequestHandler = async (
  request: vscode.ChatRequest,       // User's prompt text, command, model, references
  context: vscode.ChatContext,        // Chat history (previous turns)
  stream: vscode.ChatResponseStream,  // Output stream for UI
  token: vscode.CancellationToken     // Cancellation support
): Promise<IMyChatResult> => {
  // Implementation here
};
```

### 3.2 ChatRequest Object Properties

| Property | Type | Description |
|----------|------|-------------|
| `request.prompt` | `string` | User's text input after @participant |
| `request.command` | `string` | Slash command name (e.g., `"teach"`) |
| `request.model` | `LanguageModelChat` | The LLM user selected in dropdown |
| `request.references` | `ChatPromptReference[]` | Files/context user attached |
| `request.toolReferences` | `ChatLanguageModelToolReference[]` | Explicit tool references |

### 3.3 ChatContext - Message History

```typescript
// Access previous conversation turns
const previousRequests = context.history.filter(
  h => h instanceof vscode.ChatRequestTurn
);

const previousResponses = context.history.filter(
  h => h instanceof vscode.ChatResponseTurn
);

// Reconstruct previous assistant responses for context
previousResponses.forEach(turn => {
  let fullMessage = '';
  turn.response.forEach(r => {
    const mdPart = r as vscode.ChatResponseMarkdownPart;
    fullMessage += mdPart.value.value;
  });
  messages.push(vscode.LanguageModelChatMessage.Assistant(fullMessage));
});
```

**Important**: History is NOT automatically included in prompts — the participant must explicitly add it. [^19^] [^270^]

### 3.4 Complete Handler Example

```typescript
export function activate(context: vscode.ExtensionContext) {
  
  const handler: vscode.ChatRequestHandler = async (
    request, context, stream, token
  ): Promise<IMyChatResult> => {
    
    // 1. Determine intent from command or prompt
    if (request.command === 'teach') {
      stream.progress('Preparing lesson...');
      
      // 2. Build messages with system prompt + history + user query
      const messages = [
        vscode.LanguageModelChatMessage.User(
          'You are an expert coding tutor. Explain concepts clearly with examples.'
        )
      ];
      
      // Add chat history for context
      const prevResponses = context.history.filter(
        h => h instanceof vscode.ChatResponseTurn
      );
      prevResponses.forEach(turn => {
        let responseText = '';
        turn.response.forEach(r => {
          if (r instanceof vscode.ChatResponseMarkdownPart) {
            responseText += r.value.value;
          }
        });
        messages.push(vscode.LanguageModelChatMessage.Assistant(responseText));
      });
      
      // Add current user prompt
      messages.push(vscode.LanguageModelChatMessage.User(request.prompt));
      
      // 3. Send to LLM
      try {
        const chatResponse = await request.model.sendRequest(messages, {}, token);
        
        // 4. Stream response back to UI
        for await (const fragment of chatResponse.text) {
          stream.markdown(fragment);
        }
        
      } catch (err) {
        if (err instanceof vscode.LanguageModelError) {
          stream.markdown(`Error: ${err.message}`);
        }
      }
      
      // 5. Add interactive button
      stream.button({
        command: 'myext.openDocs',
        title: vscode.l10n.t('Open Documentation')
      });
      
      return { metadata: { command: 'teach' } };
    }
    
    // Default handler
    stream.markdown('Hello! How can I help you today?');
    return { metadata: { command: '' } };
  };
  
  // 6. Register participant
  const participant = vscode.chat.createChatParticipant(
    'myext.tutor', 
    handler
  );
  participant.iconPath = vscode.Uri.joinPath(context.extensionUri, 'icon.png');
  
  // 7. Follow-up provider
  participant.followupProvider = {
    provideFollowups(result, context, token) {
      return [{
        prompt: 'Explain it with a code example',
        label: vscode.l10n.t('Show me code'),
        command: 'teach'
      } satisfies vscode.ChatFollowup];
    }
  };
  
  // 8. Feedback collection
  context.subscriptions.push(
    participant.onDidReceiveFeedback((feedback: vscode.ChatResultFeedback) => {
      console.log(`User feedback: ${feedback.kind}`); // 'helpful' | 'unhelpful'
    })
  );
  
  context.subscriptions.push(participant);
}
```

---

## 4. Language Model API - Sending Requests

### 4.1 Model Selection

```typescript
// Select specific model family
const [model] = await vscode.lm.selectChatModels({
  vendor: 'copilot',
  family: 'gpt-4o'
});

// Select any Copilot model
const models = await vscode.lm.selectChatModels({ vendor: 'copilot' });

// In a ChatRequestHandler, use the user's selected model (recommended)
const model = request.model; // Already set by user dropdown
```

**Supported model families**: `gpt-4o`, `gpt-4o-mini`, `gpt-4`, `gpt-5`, `gpt-5-mini`, `o1`, `o1-mini`, `o3-mini`, `o4-mini`, `claude-3.5-sonnet`, `claude-3.7-sonnet`, `claude-sonnet-4`, `gemini-2.0-flash-001`, `gemini-2.5-pro` [^433^] [^274^]

### 4.2 Send Request with Messages

```typescript
const messages = [
  vscode.LanguageModelChatMessage.User('You are a helpful coding assistant.'),
  vscode.LanguageModelChatMessage.User('Explain recursion in Python.')
];

// Options: { justification?: string, modelOptions?: {}, toolMode?: ..., tools?: [...] }
const response = await model.sendRequest(messages, {}, token);

// Stream text response
for await (const fragment of response.text) {
  stream.markdown(fragment);
}
```

### 4.3 Message Types

```typescript
// User message (instructions + queries)
vscode.LanguageModelChatMessage.User('System prompt or user query');

// Assistant message (for history/context)
vscode.LanguageModelChatMessage.Assistant('Previous AI response');

// Tool call (assistant requesting tool execution)
vscode.LanguageModelChatMessage.Assistant([
  new vscode.LanguageModelToolCallPart('toolCallId', 'toolName', { arg1: 'value' })
]);

// Tool result (user message containing tool output)
vscode.LanguageModelChatMessage.User([
  new vscode.LanguageModelToolResultPart('toolCallId', [
    new vscode.LanguageModelTextPart('Tool output here')
  ])
]);
```

**Note**: The Language Model API does NOT support traditional system messages. Use a `User` message at the start of the conversation for system instructions. [^266^]

### 4.4 Error Handling

```typescript
try {
  const response = await model.sendRequest(messages, {}, token);
  // ...
} catch (err) {
  if (err instanceof vscode.LanguageModelError) {
    console.log(err.message, err.code, err.cause);
    // Error codes: 'NoPermissions', 'quota_exceeded', 'off_topic', etc.
    if (err.cause instanceof Error && err.cause.message.includes('off_topic')) {
      stream.markdown('I\'m sorry, I can only explain computer science concepts.');
    }
  } else {
    throw err; // Re-throw non-LM errors
  }
}
```

---

## 5. ChatResponseStream - Streaming Rich Responses

### 5.1 Available Stream Methods

| Method | Purpose | Example |
|--------|---------|---------|
| `stream.markdown(text)` | Render markdown text | `stream.markdown('**Bold** text')` |
| `stream.progress(msg)` | Show progress indicator | `stream.progress('Connecting...')` |
| `stream.button(command)` | Add clickable button | `stream.button({command: 'cmd', title: 'Click me'})` |
| `stream.reference(uri)` | Add context reference | `stream.reference(fileUri)` |
| `stream.anchor(uri, title)` | Inline reference link | `stream.anchor(symbolLocation, 'MySymbol')` |
| `stream.filetree(tree, baseUri)` | Show file tree | `stream.filetree(tree, workspaceUri)` |
| `stream.push(part)` | Push raw ChatResponsePart | Low-level, type-safe alternative |

### 5.2 Progress Messages

```typescript
stream.progress('Connecting to the database...');
// ...do work...
stream.progress('Fetching results...');
// ...do more work...
```

### 5.3 Interactive Buttons

```typescript
// Button executes a VS Code command when clicked
stream.button({
  command: 'myExtension.openSettings',
  title: vscode.l10n.t('Open Settings'),
  // Optional: arguments passed to command handler
  arguments: ['param1', 'param2']
});

// Register the command handler
vscode.commands.registerCommand('myExtension.openSettings', (arg1, arg2) => {
  vscode.commands.executeCommand('workbench.action.openSettings');
});
```

### 5.4 File Trees

```typescript
const tree: vscode.ChatResponseFileTree[] = [
  {
    name: 'myworkspace',
    children: [
      { name: 'README.md' },
      { name: 'src', children: [{ name: 'app.js' }] },
      { name: 'package.json' }
    ]
  }
];

stream.filetree(tree, vscode.Uri.file('/path/to/workspace'));
```

### 5.5 References

```typescript
// Reference a file
const fileUri = vscode.Uri.file('/path/to/workspace/app.js');
stream.reference(fileUri);

// Reference a specific code range
const fileRange = new vscode.Range(0, 0, 3, 0);
stream.reference(new vscode.Location(fileUri, fileRange));

// Reference external URL
stream.reference(vscode.Uri.parse('https://code.visualstudio.com'));
```

---

## 6. Calling External HTTP Services (A2A Pattern)

Chat participants run in the VS Code extension host (Node.js environment), so standard HTTP clients work:

### 6.1 Using Native fetch (Node.js 18+)

```typescript
const handler: vscode.ChatRequestHandler = async (request, context, stream, token) => {
  stream.progress('Consulting external expert agent...');
  
  try {
    // Call external A2A agent or service
    const response = await fetch('https://api.example.com/a2a/agent', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${apiKey}`
      },
      body: JSON.stringify({
        query: request.prompt,
        context: { history: context.history }
      })
    });
    
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }
    
    const data = await response.json();
    stream.markdown(data.answer);
    
    // Reference external source
    if (data.sourceUrl) {
      stream.reference(vscode.Uri.parse(data.sourceUrl));
    }
    
  } catch (err) {
    stream.markdown(`Failed to reach external agent: ${(err as Error).message}`);
  }
  
  return { metadata: { command: '' } };
};
```

### 6.2 Using axios (installed dependency)

```typescript
import axios from 'axios';

// In handler:
const response = await axios.post('https://api.example.com/query', {
  prompt: request.prompt
}, {
  headers: { 'Authorization': `Bearer ${token}` },
  timeout: 30000
});

stream.markdown(response.data.result);
```

### 6.3 Streaming from External API

```typescript
// For streaming external responses, pipe through
const externalResponse = await fetch('https://api.example.com/stream');
const reader = externalResponse.body?.getReader();

while (reader) {
  const { done, value } = await reader.read();
  if (done) break;
  const chunk = new TextDecoder().decode(value);
  stream.markdown(chunk);
}
```

**Important**: VS Code's built-in `fetch` now supports proxy settings via `http.fetchAdditionalSupport` as of v1.96. [^354^]

---

## 7. Registering and Using Tools Within Participants

### 7.1 Tool Registration Flow

```
1. Define tool in package.json (languageModelTools contribution)
2. Implement LanguageModelTool<T> interface
3. Register at runtime with vscode.lm.registerTool()
4. LLM automatically decides when to call the tool
5. Tool's invoke() method executes and returns result
```

### 7.2 Tool Implementation (Full Example)

```typescript
// 1. Define input parameters interface
export interface ITabCountParameters {
  tabGroup?: number;
}

// 2. Implement the tool class
class TabCountTool implements vscode.LanguageModelTool<ITabCountParameters> {
  
  // Optional: Custom confirmation message
  async prepareInvocation(
    options: vscode.LanguageModelToolInvocationPrepareOptions<ITabCountParameters>,
    _token: vscode.CancellationToken
  ) {
    const confirmationMessages = {
      title: 'Count the number of open tabs',
      message: new vscode.MarkdownString(
        `Count the number of open tabs?` +
        (options.input.tabGroup !== undefined
          ? ` in tab group ${options.input.tabGroup}`
          : '')
      ),
    };
    return {
      invocationMessage: 'Counting the number of tabs',
      confirmationMessages,
    };
  }
  
  // Execute the tool
  async invoke(
    options: vscode.LanguageModelToolInvocationOptions<ITabCountParameters>,
    _token: vscode.CancellationToken
  ) {
    const params = options.input;
    if (typeof params.tabGroup === 'number') {
      const group = vscode.window.tabGroups.all[Math.max(params.tabGroup - 1, 0)];
      return new vscode.LanguageModelToolResult([
        new vscode.LanguageModelTextPart(`There are ${group.tabs.length} tabs open.`)
      ]);
    } else {
      const group = vscode.window.tabGroups.activeTabGroup;
      return new vscode.LanguageModelToolResult([
        new vscode.LanguageModelTextPart(`There are ${group.tabs.length} tabs open.`)
      ]);
    }
  }
}

// 3. Register in activate()
export function activate(context: vscode.ExtensionContext) {
  context.subscriptions.push(
    vscode.lm.registerTool('myext_tabCount', new TabCountTool())
  );
}
```

### 7.3 Tool-Calling with @vscode/chat-extension-utils

```typescript
import * as chatUtils from '@vscode/chat-extension-utils';

const handler: vscode.ChatRequestHandler = async (request, chatContext, stream, token) => {
  
  // Filter available tools by tag
  const tools = request.command === 'all'
    ? vscode.lm.tools
    : vscode.lm.tools.filter(tool => tool.tags.includes('myext'));
  
  // sendChatParticipantRequest handles the ENTIRE tool-calling loop:
  // - Prompt crafting with history
  // - Sending to LLM with tool definitions
  // - Executing tool calls
  // - Streaming results back
  const libResult = chatUtils.sendChatParticipantRequest(
    request,
    chatContext,
    {
      prompt: 'You are a helpful coding assistant with access to workspace tools.',
      responseStreamOptions: {
        stream,
        references: true,   // Auto-stream references
        responseText: true  // Auto-stream text response
      },
      tools
    },
    token
  );
  
  // Return result (contains error details, tool-calling metadata)
  return await libResult.result;
};
```

### 7.4 Manual Tool Calling (More Control)

```typescript
const handler: vscode.ChatRequestHandler = async (request, context, stream, token) => {
  const model = request.model;
  const tools = vscode.lm.tools.filter(t => t.tags.includes('myext'));
  
  const messages = [
    vscode.LanguageModelChatMessage.User('You are a helpful assistant.'),
    vscode.LanguageModelChatMessage.User(request.prompt)
  ];
  
  // Send request WITH tool definitions
  const response = await model.sendRequest(messages, { tools }, token);
  
  // Process the stream - may contain text or tool calls
  for await (const chunk of response.stream) {
    if (chunk instanceof vscode.LanguageModelTextPart) {
      stream.markdown(chunk.value);
    } else if (chunk instanceof vscode.LanguageModelToolCallPart) {
      // Execute the tool
      const toolResult = await vscode.lm.invokeTool(
        chunk.name,
        { input: chunk.input },
        token
      );
      
      // Send tool result back to LLM
      messages.push(vscode.LanguageModelChatMessage.Assistant([chunk]));
      messages.push(vscode.LanguageModelChatMessage.User([
        new vscode.LanguageModelToolResultPart(chunk.callId, toolResult.content)
      ]));
      
      // Get continuation from LLM
      const continuation = await model.sendRequest(messages, { tools }, token);
      for await (const fragment of continuation.text) {
        stream.markdown(fragment);
      }
    }
  }
};
```

---

## 8. @vscode/chat-extension-utils Library

### 8.1 What It Is

`@vscode/chat-extension-utils` is an **official Microsoft npm package** that simplifies building high-quality chat participants. It was released with VS Code 1.96 (November 2024). [^354^] [^271^]

**Install**: `npm install @vscode/chat-extension-utils`

### 8.2 What It Handles

| Aspect | Without Library | With `@vscode/chat-extension-utils` |
|--------|----------------|-----------------------------------|
| Tool calling loop | Manual iteration | `sendChatParticipantRequest()` handles it |
| Prompt crafting | Manual message assembly | Auto-includes history, references, tool calls |
| Response streaming | Manual `for await` loop | `responseStreamOptions` auto-streams |
| Reference tracking | Manual `stream.reference()` | `references: true` auto-tracks |
| Model selection | `request.model` or `selectChatModels()` | Uses `request.model` by default |

### 8.3 API Reference

```typescript
import * as chatUtils from '@vscode/chat-extension-utils';

// Main function - handles entire request lifecycle
const libResult = chatUtils.sendChatParticipantRequest(
  request: vscode.ChatRequest,
  chatContext: vscode.ChatContext,
  options: ChatHandlerOptions,
  token: vscode.CancellationToken
);

// Returns: { result: Promise<ChatResult> }
return await libResult.result;
```

### 8.4 ChatHandlerOptions

```typescript
interface ChatHandlerOptions<T extends PromptElement = PromptElement> {
  /** System instructions / personality for the participant */
  prompt?: string | PromptElementAndProps<T>;
  
  /** Override model (defaults to request.model) */
  model?: vscode.LanguageModelChat;
  
  /** Tools to make available for this request */
  tools?: ReadonlyArray<vscode.LanguageModelChatTool | AdHocChatTool<object>>;
  
  /** Justification for the request (shown to user) */
  requestJustification?: string;
  
  /** Enable auto-streaming of response */
  responseStreamOptions?: {
    stream: vscode.ChatResponseStream;
    references?: boolean;   // Auto-stream references used
    responseText?: boolean; // Auto-stream markdown text
  };
  
  /** Extension mode for debug trace logging */
  extensionMode?: vscode.ExtensionMode;
}
```

### 8.5 Additional Components

The package also exports prompt-tsx compatible components for advanced prompt building: [^271^]

```typescript
// Available from @vscode/chat-extension-utils
import { UserMessage } from '@vscode/chat-extension-utils/dist/promptTsx';
import { FileContext } from '@vscode/chat-extension-utils';
import { History } from '@vscode/chat-extension-utils';
import { ToolCalls } from '@vscode/chat-extension-utils';
import { Tags } from '@vscode/chat-extension-utils';
import { FileTree } from '@vscode/chat-extension-utils';
```

---

## 9. Packaging and Publishing to VS Code Marketplace

### 9.1 Development Setup

```bash
# Generate extension scaffold
npx --package yo --package generator-code -- yo code
# Select: New Extension (TypeScript)

# Install chat dependencies
npm install @vscode/chat-extension-utils
npm install @vscode/prompt-tsx   # Optional, for advanced prompts

# Engine requirement for chat API
# package.json: "engines": { "vscode": "^1.93.0" }
```

### 9.2 package.json Requirements for Marketplace

```json
{
  "name": "my-chat-extension",
  "displayName": "My Chat Extension",
  "description": "An expert agent for VS Code Copilot Chat",
  "version": "0.0.1",
  "publisher": "your-publisher-id",
  "engines": { "vscode": "^1.93.0" },
  "categories": ["AI", "Chat"],
  "keywords": ["copilot", "chat", "ai", "agent"],
  "icon": "resources/icon.png",
  "galleryBanner": { "color": "#1e1e1e" },
  "repository": { "type": "git", "url": "https://github.com/your/repo" },
  "bugs": { "url": "https://github.com/your/repo/issues" },
  "license": "MIT",
  "main": "./out/extension.js",
  "activationEvents": [],
  "scripts": {
    "vscode:prepublish": "npm run compile",
    "compile": "tsc -p ./",
    "watch": "tsc -watch -p ./"
  },
  "devDependencies": {
    "@types/vscode": "^1.93.0",
    "typescript": "^5.0.0"
  }
}
```

### 9.3 Publishing Steps

```bash
# 1. Install vsce CLI
npm install -g @vscode/vsce

# 2. Package locally (creates .vsix file)
vsce package

# 3. Create Azure DevOps PAT
# Go to https://dev.azure.com -> User Settings -> Personal Access Tokens
# Scope: Marketplace -> Manage

# 4. Login to publisher
vsce login <publisher-id>

# 5. Publish
vsce publish
# or specific version bump:
vsce publish patch   # 0.0.1 -> 0.0.2
vsce publish minor   # 0.0.1 -> 0.1.0
vsce publish major   # 0.0.1 -> 1.0.0

# 6. Pre-release versions
vsce publish --pre-release
```

### 9.4 Marketplace Requirements

| Requirement | Details |
|-------------|---------|
| Icon | PNG format, minimum 128x128px |
| README images | Must use `https://` URLs (no SVGs) |
| Keywords | Maximum 30 tags |
| Publisher ID | Must match Azure DevOps/Marketplace account |
| `vscode:prepublish` | Runs automatically before package/publish |

[^279^] [^355^]

---

## 10. Participant Auto-Detection (Without @-Mention)

### 10.1 How It Works

Participant auto-detection allows VS Code to automatically route queries to participants **without** explicit @-mention. VS Code uses the `disambiguation` property to match natural language queries to the most suitable participant. [^19^]

### 10.2 Disambiguation Configuration

```json
{
  "contributes": {
    "chatParticipants": [
      {
        "id": "myext.codeReviewer",
        "name": "reviewer",
        "fullName": "Code Reviewer",
        "description": "Review your code for quality and best practices",
        "isSticky": true,
        "disambiguation": [
          {
            "category": "code_review",
            "description": "The user wants a code review, quality assessment, or suggestions for improving their code.",
            "examples": [
              "Review this function for bugs",
              "Is this code following best practices?",
              "How can I improve this implementation?",
              "Check my code for security issues"
            ]
          }
        ],
        "commands": [
          {
            "name": "security",
            "description": "Focus on security vulnerabilities",
            "disambiguation": [
              {
                "category": "security_review",
                "description": "The user wants a security-focused code review.",
                "examples": [
                  "Check for SQL injection vulnerabilities",
                  "Review authentication logic"
                ]
              }
            ]
          }
        ]
      }
    ]
  }
}
```

### 10.3 Detection Accuracy Guidelines

1. **Be specific** in descriptions — avoid generic terminology that conflicts with built-in participants
2. **Use diverse examples** covering synonyms and variations
3. **Write natural language** descriptions as if explaining to a user
4. **Test extensively** with variations of example questions
5. **Built-in participants take precedence** — a workspace-focused participant may conflict with `@workspace` [^19^]

### 10.4 Current Limitations

> **Important**: As of early 2025, community reports indicate that auto-detection via `disambiguation` may not always trigger automatically without @-mention in all VS Code versions. Built-in participants like `@workspace` and `@terminal` are reliably detected. Extension-contributed participants should primarily expect @-mention invocation. Test thoroughly in your target VS Code version. [^434^] [^371^]

---

## 11. MCP Server Integration

### 11.1 MCP Server Types in VS Code

VS Code supports three types of tools: [^264^]

| Type | Source | Distribution |
|------|--------|-------------|
| Built-in tools | VS Code core | Built into VS Code |
| Extension tools | VS Code extensions | Via VS Code Marketplace |
| MCP tools | MCP servers | Cross-platform, any MCP client |

### 11.2 Adding MCP Servers to VS Code

**Via `.vscode/mcp.json` (workspace-level):**
```json
{
  "servers": {
    "playwright": {
      "command": "npx",
      "args": ["-y", "@microsoft/mcp-server-playwright"]
    },
    "fetch": {
      "command": "uvx",
      "args": ["mcp-server-fetch"]
    }
  }
}
```

**Via Command Line:**
```bash
code --add-mcp '{"name":"my-server","command":"uvx","args":["mcp-server-fetch"]}'
```

**Via Installation URL:**
```typescript
const link = `vscode:mcp/install?${encodeURIComponent(JSON.stringify(serverConfig))}`;
// Opens VS Code and installs the MCP server
```

**Via Extension (Programmatic):**
```typescript
export function activate(context: vscode.ExtensionContext) {
  context.subscriptions.push(
    vscode.lm.registerMcpServerDefinitionProvider('myProvider', {
      onDidChangeMcpServerDefinitions: didChangeEmitter.event,
      
      provideMcpServerDefinitions: async () => [
        new vscode.McpStdioServerDefinition({
          label: 'myLocalServer',
          command: 'node',
          args: ['server.js'],
          cwd: vscode.Uri.file('/path/to/server'),
          env: { API_KEY: '' },
          version: '1.0.0'
        }),
        new vscode.McpHttpServerDefinition({
          label: 'myRemoteServer',
          uri: 'http://localhost:3000',
          headers: { 'API_VERSION': '1.0.0' },
          version: '1.0.0'
        })
      ],
      
      resolveMcpServerDefinition: async (server) => {
        // Prompt for API key if needed
        const apiKey = await vscode.window.showInputBox({
          prompt: 'Enter API key for MCP server'
        });
        // Return resolved server or undefined to cancel
        return server;
      }
    })
  );
}
```

### 11.3 MCP Server Discovery

VS Code can auto-discover MCP servers from other tools like Claude Desktop:
```json
{
  "chat.mcp.discovery.enabled": ["claudeDesktop", "continue"]
}
```

### 11.4 Managing MCP Servers

- Open Extensions view (Ctrl+Shift+X) and type `@mcp` to browse MCP gallery
- Use **MCP: List Servers** command from Command Palette
- Start/stop/restart from `.vscode/mcp.json` editor toolbar buttons
- Toggle individual tools via **Configure Tools** in chat input [^229^]

---

## 12. Follow-up Questions

### 12.1 Follow-up Provider

```typescript
interface IMyChatResult extends vscode.ChatResult {
  metadata: { command: string };
}

participant.followupProvider = {
  provideFollowups(result: IMyChatResult, context: vscode.ChatContext, token: vscode.CancellationToken) {
    if (result.metadata.command === 'teach') {
      return [
        {
          prompt: 'Give me a code exercise for this',
          label: vscode.l10n.t('Practice with exercise'),
          command: 'exercise'  // Routes to /exercise command
        },
        {
          prompt: 'Explain the key concepts again',
          label: vscode.l10n.t('Review concepts')
          // No command = routes to default handler
        }
      ];
    }
    return undefined;
  }
};
```

### 12.2 ChatFollowup Interface

```typescript
interface ChatFollowup {
  prompt: string;        // Text sent when clicked
  label?: string;        // Display label (defaults to prompt)
  participant?: string;  // Target participant ID (same extension only)
  command?: string;      // Target slash command
}
```

---

## 13. Limitations & Gotchas

### API Limitations

| Limitation | Details |
|-----------|---------|
| No system messages | Use `User` message for system prompts [^266^] |
| History not auto-included | Must manually add `context.history` to prompts [^270^] |
| Consent required | Users must approve extension access to Copilot LLMs |
| Model availability | `selectChatModels()` can return empty array; handle gracefully |
| Tool confirmation | Extension tools always show confirmation dialog (user can "Always Allow") |
| `registerTool` vs package.json | Both definition in `package.json` AND `registerTool()` call needed |
| Participant names | Some names are reserved; conflicts show fully-qualified ID |

### Error Codes (LanguageModelError)

| Code | Meaning | Action |
|------|---------|--------|
| `NoPermissions` | User hasn't granted access | Show setup instructions |
| `quota_exceeded` | Rate limit hit | Suggest retry later |
| `off_topic` | Content filter triggered | Show appropriate message |

### Development Gotchas

1. **`engines.vscode` minimum**: Use `^1.93.0` or higher for Chat API stability [^274^]
2. **Activation events**: Modern VS Code auto-activates on `chatParticipants` contribution; `activationEvents: []` is fine
3. **Icon paths**: Use `vscode.Uri.joinPath(context.extensionUri, 'icon.png')` for participant icons
4. **Theme icons**: Can use `new vscode.ThemeIcon('sparkle')` instead of file icons
5. **Streaming order**: Call `stream.progress()` BEFORE starting async work
6. **Tool naming**: Use `{verb}_{noun}` format (e.g., `get_weather`, `count_tabs`) [^265^]
7. **Bundling**: Do NOT bundle `vscode` in webpack — mark as external

---

## 14. Recommendations for Prototype

### Minimal Viable Participant

```typescript
// src/extension.ts - Complete minimal participant
import * as vscode from 'vscode';

interface IResult extends vscode.ChatResult {
  metadata: { command: string };
}

export function activate(context: vscode.ExtensionContext) {
  const handler: vscode.ChatRequestHandler = async (request, context, stream, token) => {
    const messages = [
      vscode.LanguageModelChatMessage.User(
        'You are a helpful multi-agent coordinator. Help users work with AI agents.'
      ),
      vscode.LanguageModelChatMessage.User(request.prompt)
    ];
    
    try {
      const response = await request.model.sendRequest(messages, {}, token);
      for await (const fragment of response.text) {
        stream.markdown(fragment);
      }
    } catch (err) {
      stream.markdown('Error processing request. Please try again.');
    }
    
    return { metadata: { command: request.command || '' } };
  };
  
  const participant = vscode.chat.createChatParticipant('multiagent.coordinator', handler);
  participant.iconPath = new vscode.ThemeIcon('hubot');
  participant.description = 'Coordinate with multiple AI agents';
  participant.isSticky = true;
  
  context.subscriptions.push(participant);
}

export function deactivate() {}
```

```json
// package.json
{
  "name": "multiagent-coordinator",
  "displayName": "Multi-Agent Coordinator",
  "version": "0.0.1",
  "publisher": "your-publisher",
  "engines": { "vscode": "^1.93.0" },
  "categories": ["AI", "Chat"],
  "activationEvents": [],
  "main": "./out/extension.js",
  "contributes": {
    "chatParticipants": [
      {
        "id": "multiagent.coordinator",
        "name": "coordinator",
        "fullName": "Agent Coordinator",
        "description": "Coordinate tasks across multiple AI agents",
        "isSticky": true,
        "commands": [
          { "name": "delegate", "description": "Delegate task to a specific agent" },
          { "name": "status", "description": "Check status of all agents" }
        ]
      }
    ]
  }
}
```

### Recommended Technology Stack

| Component | Recommendation |
|-----------|---------------|
| VS Code version target | `^1.96.0` (stable Chat + Tool API) |
| Tool calling | `@vscode/chat-extension-utils` for simplicity |
| Advanced prompts | `@vscode/prompt-tsx` for token-aware prompting |
| HTTP calls | Native `fetch()` (proxy-aware since v1.96) |
| Testing | Press `F5` for Extension Development Host |
| Publishing | `vsce` CLI with Azure DevOps PAT |

---

## 15. Sources & References

| # | Source | Authority | Key Content |
|---|--------|-----------|-------------|
| [^19^] | https://code.visualstudio.com/api/extension-guides/ai/chat | Official (A) | Complete Chat Participant API guide |
| [^270^] | https://code.visualstudio.com/api/extension-guides/ai/chat-tutorial | Official (A) | Step-by-step tutorial building a participant |
| [^266^] | https://code.visualstudio.com/api/extension-guides/ai/language-model | Official (A) | Language Model API guide |
| [^265^] | https://code.visualstudio.com/api/extension-guides/ai/tools | Official (A) | Language Model Tool API guide |
| [^264^] | https://code.visualstudio.com/api/extension-guides/ai/mcp | Official (A) | MCP server developer guide |
| [^271^] | https://github.com/microsoft/vscode-chat-extension-utils | GitHub (S) | @vscode/chat-extension-utils library |
| [^449^] | https://github.com/microsoft/vscode-extension-samples/blob/main/chat-sample/src/simple.ts | GitHub (S) | Official sample - simple participant |
| [^453^] | https://github.com/microsoft/vscode-extension-samples/blob/main/chat-sample/src/chatUtilsSample.ts | GitHub (S) | Official sample - chat utils with tools |
| [^455^] | https://github.com/microsoft/vscode-extension-samples/blob/main/chat-sample/src/tools.ts | GitHub (S) | Official sample - tool implementation |
| [^279^] | https://code.visualstudio.com/api/working-with-extensions/publishing-extension | Official (A) | Publishing extensions guide |
| [^338^] | https://code.visualstudio.com/api/references/vscode-api | Official (A) | VS Code API reference (ChatParticipant, ChatResponseStream) |
| [^274^] | https://techcommunity.microsoft.com/blog/educatordeveloperblog/create-your-own-visual-studio-code-chat-participant-with-phi-3-5-by-github-model/4247224 | TechCommunity (A) | Full participant with external API calls |
| [^284^] | https://eclipsesource.com/blogs/2026/03/19/domain-specific-ai-extensions-vs-code/ | Blog (NA) | Architecture overview of VS Code AI stack |
| [^269^] | https://vogella.com/blog/vscode_copilot_extension/ | Blog (NA) | Complete tool-calling walkthrough |
| [^229^] | https://code.visualstudio.com/docs/copilot/customization/mcp-servers | Official (A) | MCP server user docs |
| [^354^] | https://code.visualstudio.com/updates/v1_96 | Official (A) | v1.96 release notes (chat-extension-utils) |
| [^447^] | https://pascoal.net/2024/12/08/gh-copilot-extension-vscode-followups/ | Blog (NA) | Follow-up provider deep dive |
| [^355^] | https://cloud-authority.com/how-i-built-and-published-a-vs-code-extension-to-the-marketplace | Blog (NA) | End-to-end publishing walkthrough |
| [^371^] | https://github.com/microsoft/vscode-discussions/discussions/2980 | GitHub Discussions (S) | Disambiguation/participant detection Q&A |
| [^434^] | https://github.com/microsoft/vscode/issues/267734 | GitHub Issues (S) | Follow-up provider rendering issue |
| [^433^] | https://github.com/microsoft/vscode/issues/263613 | GitHub Issues (S) | Test CLI permissions for chat extensions |

---

*Research completed. All findings based on official VS Code documentation (code.visualstudio.com), Microsoft GitHub repositories, and authoritative community sources as of June 2025.*
