# React Chat Interface & Real-time Agent Communication

## Executive Summary

Building a React-based chat interface for multi-agent communication requires combining several specialized libraries and patterns. The key finding is that **Assistant-UI** is the most mature library for AI chat UIs in 2025 [^524^], with the **Vercel AI SDK** providing the most battle-tested streaming infrastructure [^550^]. For A2A/MCP agent communication, the protocol natively uses **Server-Sent Events (SSE)** for streaming [^10^], which aligns well with React's streaming patterns via `useChat` hooks or custom `useSSE` hooks.

The recommended stack for a multi-agent chat prototype:
- **UI Library**: Assistant-UI (`@assistant-ui/react`) or Shadcn Chat blocks [^620^]
- **Streaming**: SSE via native `EventSource` or `useEventSource` from ReactUse [^559^]
- **Markdown Rendering**: `react-markdown` + `react-syntax-highlighter` [^543^]
- **Mentions**: `react-mentions` package [^538^]
- **State Management**: Zustand for conversation state [^562^]
- **Agent Backend**: A2A protocol via `@assistant-ui/react-a2a` or `use-mcp` [^598^]

---

## 1. React Chat UI Component Libraries

### 1.1 Top Recommendation: Assistant-UI

**Assistant-UI** is the dominant React library for AI chat interfaces in 2025 [^524^]. It provides composable primitives inspired by Radix UI rather than monolithic components.

**Installation:**
```bash
# New project
npx assistant-ui@latest create

# Or add to existing project
npx assistant-ui@latest init
```

**Key Features [^524^]:**
- Streaming LLM responses with auto-scrolling
- Markdown and code syntax highlighting
- File attachments, keyboard shortcuts, accessibility
- Support for 14+ model providers (OpenAI, Anthropic, Google, etc.)
- Framework integration: Vercel AI SDK, LangGraph, Mastra
- Generative UI mapping tool calls to custom components
- First-class A2A runtime support via `@assistant-ui/react-a2a` [^603^]

**Basic Setup (Next.js):**
```tsx
import { openai } from "@ai-sdk/openai";
import { streamText, convertToModelMessages } from "ai";

// app/api/chat/route.ts
export const maxDuration = 30;

export async function POST(req: Request) {
  const { messages, system, tools } = await req.json();
  const result = streamText({
    model: openai("gpt-4o-mini"),
    system,
    messages: await convertToModelMessages(messages),
    tools,
  });
  return result.toUIMessageStreamResponse();
}
```

### 1.2 MUI X Chat

Material-UI's chat component (`@mui/x-chat`) is in alpha as of 2025, offering enterprise-grade chat primitives [^611^]:

```tsx
import { ChatCodeBlock } from '@mui/x-chat';
import { codeToHtml } from 'shiki';

// Code block with syntax highlighting
<ChatCodeBlock language="typescript">
  {`const greet = (name: string) => \`Hello, \${name}!\`;`}
</ChatCodeBlock>
```

Features include automatic code fence rendering, language labels, copy-to-clipboard buttons, and pluggable highlighters (Shiki, Prism, highlight.js) [^529^].

### 1.3 CopilotKit

**CopilotKit** provides pre-built, full-stack agentic chat components [^609^]:

```bash
npx copilotkit@latest create -f nextjs
```

Available components [^614^]:
- `CopilotChat` - flexible chat component
- `CopilotSidebar` - collapsible sidebar chat
- `CopilotPopup` - floating chat bubble

```tsx
import { CopilotSidebar } from "@copilotkit/react-ui";

<CopilotSidebar
  defaultOpen={true}
  labels={{
    modalHeaderTitle: "Sidebar Assistant",
    welcomeMessageText: "How can I help you today?",
  }}
>
  <YourApp />
</CopilotSidebar>
```

### 1.4 Shadcn Chat (Community)

Community-built shadcn chat components [^620^] [^622^]:

```bash
npx shadcn add prompt-kit/prompt-input
npx shadcn add prompt-kit/message
npx shadcn add prompt-kit/code-block
```

**Prompt Kit** [^620^] offers:
- Prompt Input for text entry
- Message component with markdown rendering
- CodeBlock with syntax highlighting
- File Upload with drag-and-drop
- Response Stream for streaming animations
- Reasoning component for AI agent thought display

### 1.5 Comparison Table

| Library | Maturity | Streaming | A2A Support | Cost | Best For |
|---------|----------|-----------|-------------|------|----------|
| Assistant-UI | High (production) | Native | First-class (`@assistant-ui/react-a2a`) | Free (MIT) | Multi-agent A2A chat |
| CopilotKit | High | Native | Via AG-UI protocol | Free (open core) | Full-stack agent apps |
| MUI X Chat | Alpha | Yes | None | Free (MIT) | Enterprise MUI apps |
| Shadcn Chat | Community | Manual | Manual | Free (MIT) | Custom built UIs |

---

## 2. SSE (Server-Sent Events) Implementation for Streaming

### 2.1 Why SSE for Agent Communication

A2A protocol uses **SSE** as its default streaming transport [^10^] [^120^]. SSE is ideal for agent communication because:
- **Unidirectional** (server pushes to client) matches the agent → UI pattern
- **Built-in auto-reconnection** with `EventSource` API
- **Simple HTTP-based** - works through firewalls and proxies
- **Text-based** - ideal for streaming markdown/text responses
- **Native browser support** - no library needed on the client

### 2.2 Basic SSE in React

```tsx
import { useState, useEffect } from 'react';

function useSSE(url: string) {
  const [data, setData] = useState<any[]>([]);
  const [status, setStatus] = useState<'connecting' | 'open' | 'closed' | 'error'>('connecting');

  useEffect(() => {
    const eventSource = new EventSource(url);

    eventSource.onopen = () => setStatus('open');
    eventSource.onmessage = (event) => {
      const parsed = JSON.parse(event.data);
      setData((prev) => [...prev, parsed]);
    };
    eventSource.onerror = () => setStatus('error');

    return () => {
      eventSource.close();
      setStatus('closed');
    };
  }, [url]);

  return { data, status };
}
```

### 2.3 Advanced SSE Hook with Reconnection

From [^515^] - production-ready pattern:

```tsx
import { useState, useEffect, useCallback, useRef } from 'react';

interface UseSSEOptions {
  url: string;
  onMessage?: (data: any) => void;
  onStatus?: (eventType: string, data: any) => void;
  reconnect?: boolean;
}

function useSSEStream<T>(options: UseSSEOptions) {
  const { url, onMessage, onStatus, reconnect = true } = options;
  const [data, setData] = useState<T | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const retryCount = useRef(0);
  const maxRetries = 5;
  const eventSourceRef = useRef<EventSource | null>(null);

  const connect = useCallback(() => {
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
    }

    const es = new EventSource(url);
    eventSourceRef.current = es;

    es.onopen = () => {
      setIsConnected(true);
      setError(null);
      retryCount.current = 0;
    };

    es.addEventListener('status', (event) => {
      const parsed = JSON.parse(event.data);
      onStatus?.('status', parsed);
    });

    es.addEventListener('artifact', (event) => {
      const parsed = JSON.parse(event.data);
      onStatus?.('artifact', parsed);
    });

    es.onmessage = (event) => {
      const parsed = JSON.parse(event.data);
      setData(parsed);
      onMessage?.(parsed);
    };

    es.onerror = () => {
      setIsConnected(false);
      es.close();

      if (reconnect && retryCount.current < maxRetries) {
        const backoff = Math.min(1000 * 2 ** retryCount.current, 30000);
        retryCount.current++;
        setTimeout(connect, backoff);
      } else {
        setError(new Error('SSE connection failed'));
      }
    };
  }, [url, onMessage, onStatus, reconnect]);

  const disconnect = useCallback(() => {
    eventSourceRef.current?.close();
    setIsConnected(false);
  }, []);

  useEffect(() => {
    connect();
    return () => disconnect();
  }, [connect, disconnect]);

  return { data, isConnected, error, disconnect, reconnect: connect };
}
```

### 2.4 Using reactuse's useEventSource

From [^559^] - ready-made hook:

```tsx
import { useEventSource } from '@reactuse/core';

function AgentStream({ agentUrl }: { agentUrl: string }) {
  const { status, data, error, close, open } = useEventSource(
    `${agentUrl}/stream`,
    [],  // event types to listen for
    { immediate: true }
  );

  return (
    <div>
      <div>Connection: {status}</div>
      <div>Latest update: {JSON.stringify(data)}</div>
      {error && <div className="text-red-500">{error.message}</div>}
      <button onClick={open}>Connect</button>
      <button onClick={close}>Disconnect</button>
    </div>
  );
}
```

### 2.5 Server-Side SSE Endpoint (Node.js/Express)

```typescript
// server.ts - A2A-compatible SSE endpoint
import express from 'express';
import cors from 'cors';

const app = express();
app.use(cors());

const clients = new Map<string, express.Response>();

app.post('/tasks/sendSubscribe', (req, res) => {
  // Set SSE headers
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.setHeader('X-Accel-Buffering', 'no'); // Disable nginx buffering

  const taskId = req.body.params?.id || crypto.randomUUID();
  clients.set(taskId, res);

  // Send initial status
  sendSSEEvent(res, 'status', {
    jsonrpc: '2.0',
    id: req.body.id,
    result: {
      id: taskId,
      status: { state: 'submitted', message: { role: 'agent', parts: [{ type: 'text', text: 'Task received' }] } },
    },
  });

  // Send working status after delay
  setTimeout(() => {
    sendSSEEvent(res, 'status', {
      jsonrpc: '2.0',
      id: req.body.id,
      result: {
        id: taskId,
        status: { state: 'working', message: { role: 'agent', parts: [{ type: 'text', text: 'Processing...' }] } },
      },
    });
  }, 500);

  // Handle disconnect
  req.on('close', () => {
    clients.delete(taskId);
  });
});

function sendSSEEvent(res: express.Response, event: string, data: unknown) {
  res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
}
```

---

## 3. @-Mention System for Agent Routing

### 3.1 Using react-mentions

The `react-mentions` package [^538^] provides the most mature @-mention system for React:

```bash
npm install react-mentions
```

```tsx
import { useState, useEffect } from 'react';
import { MentionsInput, Mention } from 'react-mentions';

interface Agent {
  id: string;
  display: string;
  avatar?: string;
  description?: string;
}

function AgentMentionInput({
  agents,
  onSubmit,
}: {
  agents: Agent[];
  onSubmit: (text: string, mentionedAgentIds: string[]) => void;
}) {
  const [value, setValue] = useState('');

  const handleSubmit = () => {
    // Extract mentioned agents
    const mentionRegex = /@\[([^\]]+)\]\(([^)]+)\)/g;
    const mentionedAgentIds: string[] = [];
    let match;
    while ((match = mentionRegex.exec(value)) !== null) {
      mentionedAgentIds.push(match[2]); // capture the ID from @(id)
    }

    onSubmit(value, mentionedAgentIds);
    setValue('');
  };

  return (
    <div className="mention-input-container">
      <MentionsInput
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder="Type @ to mention an agent..."
        className="mentions"
        onKeyDown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            handleSubmit();
          }
        }}
        style={{
          control: { backgroundColor: '#fff', fontSize: 14 },
          input: { margin: 0, padding: 10 },
          suggestions: {
            list: { backgroundColor: '#fff', border: '1px solid #ddd' },
            item: { padding: '8px 12px', borderBottom: '1px solid #eee' },
          },
        }}
      >
        <Mention
          trigger="@"
          data={agents}
          appendSpaceOnAdd
          displayTransform={(id, display) => `@${display}`}
          style={{ backgroundColor: '#dbeafe' }}
          renderSuggestion={(suggestion) => (
            <div className="flex items-center gap-2 p-1">
              <img src={suggestion.avatar} className="w-6 h-6 rounded-full" />
              <div>
                <div className="font-medium">{suggestion.display}</div>
                <div className="text-xs text-gray-500">{suggestion.description}</div>
              </div>
            </div>
          )}
        />
      </MentionsInput>
      <button onClick={handleSubmit}>Send</button>
    </div>
  );
}
```

### 3.2 Agent Routing Logic

Parse mentions to route messages to specific agents:

```tsx
function parseAgentMentions(text: string): {
  cleanText: string;
  targetAgents: string[];
} {
  const targetAgents: string[] = [];

  // Extract @mentions with IDs: @[Agent Name](agent-id)
  const cleanText = text.replace(/@\[([^\]]+)\]\(([^)]+)\)/g, (_, display, id) => {
    targetAgents.push(id);
    return `@${display}`; // Keep readable name in text
  });

  return { cleanText, targetAgents };
}

// Usage in message handler
async function handleSendMessage(text: string) {
  const { cleanText, targetAgents } = parseAgentMentions(text);

  if (targetAgents.length === 0) {
    // Route to default/orchestrator agent
    await sendToAgent('orchestrator', cleanText);
  } else {
    // Route to specified agents (parallel or sequential)
    await Promise.all(
      targetAgents.map((agentId) => sendToAgent(agentId, cleanText))
    );
  }
}
```

---

## 4. Typing Indicator & Streaming Text Effects

### 4.1 Typing Indicator Component

Based on [^564^], a typing indicator for agents:

```tsx
import { useState, useEffect } from 'react';

interface TypingIndicatorProps {
  agentName: string;
  status: 'typing' | 'working' | 'thinking' | 'idle';
}

export function AgentTypingIndicator({ agentName, status }: TypingIndicatorProps) {
  const [dots, setDots] = useState('');

  useEffect(() => {
    if (status === 'idle') return;
    const interval = setInterval(() => {
      setDots((prev) => (prev.length >= 3 ? '' : prev + '.'));
    }, 500);
    return () => clearInterval(interval);
  }, [status]);

  if (status === 'idle') return null;

  const statusLabels: Record<string, string> = {
    typing: 'typing',
    working: 'working',
    thinking: 'thinking',
  };

  return (
    <div className="flex items-center gap-2 text-sm text-gray-500 animate-pulse">
      <div className="flex gap-1">
        <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style={{ animationDelay: '0ms' }} />
        <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style={{ animationDelay: '150ms' }} />
        <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style={{ animationDelay: '300ms' }} />
      </div>
      <span>{agentName} is {statusLabels[status]}{dots}</span>
    </div>
  );
}
```

### 4.2 Streaming Text with TypeIt

From [^545^] for word-by-word streaming effect:

```tsx
import { useEffect, useState, useRef } from 'react';

function useStreamingText(streamText: string, speed: number = 30) {
  const [displayedText, setDisplayedText] = useState('');
  const indexRef = useRef(0);
  const frameRef = useRef<number>();

  useEffect(() => {
    indexRef.current = 0;
    setDisplayedText('');

    const animate = () => {
      if (indexRef.current < streamText.length) {
        // Word-by-word for natural feel
        const nextSpace = streamText.indexOf(' ', indexRef.current + 1);
        const endIndex = nextSpace === -1 ? streamText.length : nextSpace + 1;
        indexRef.current = endIndex;
        setDisplayedText(streamText.slice(0, endIndex));
        frameRef.current = setTimeout(animate, speed);
      }
    };

    frameRef.current = setTimeout(animate, speed);

    return () => {
      if (frameRef.current) clearTimeout(frameRef.current);
    };
  }, [streamText, speed]);

  return displayedText;
}

// Usage
function StreamingMessage({ content }: { content: string }) {
  const displayedText = useStreamingText(content, 30);
  return <div className="prose">{displayedText}</div>;
}
```

### 4.3 Shimmer Loading Effect (Tailwind)

Assistant-UI's `tw-shimmer` plugin [^552^]:

```bash
npm install tw-shimmer
```

```css
@import "tailwindcss";
@import "tw-shimmer";
```

```tsx
<div className="shimmer-container flex gap-3">
  <div className="shimmer-bg bg-muted size-12 rounded-full" />
  <div className="flex-1 space-y-2">
    <div className="shimmer-bg bg-muted h-4 w-1/4 rounded" />
    <div className="shimmer-bg bg-muted h-4 w-full rounded" />
  </div>
</div>
```

---

## 5. Conversation State Management with Multiple Agents

### 5.1 Zustand Store for Multi-Agent Chat

Zustand is the recommended choice for chat state due to its simplicity and minimal re-renders [^562^] [^563^]:

```bash
npm install zustand
```

```typescript
// stores/chatStore.ts
import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import { devtools } from 'zustand/middleware';

export type MessageRole = 'user' | 'agent' | 'system';
export type AgentStatus = 'idle' | 'working' | 'completed' | 'error' | 'input_required';

export interface ChatMessage {
  id: string;
  role: MessageRole;
  content: string;
  agentId?: string;
  agentName?: string;
  timestamp: Date;
  isStreaming?: boolean;
  status?: AgentStatus;
  artifacts?: Artifact[];
  metadata?: Record<string, any>;
}

export interface AgentInfo {
  id: string;
  name: string;
  avatar?: string;
  description: string;
  status: AgentStatus;
  capabilities: string[];
}

export interface Artifact {
  id: string;
  type: 'text' | 'code' | 'file' | 'image';
  name: string;
  content: string;
  language?: string;
}

interface ChatState {
  // Messages
  messages: ChatMessage[];
  addMessage: (message: Omit<ChatMessage, 'id' | 'timestamp'>) => void;
  updateMessage: (id: string, updates: Partial<ChatMessage>) => void;
  appendToMessage: (id: string, content: string) => void;
  setMessageStatus: (id: string, status: AgentStatus) => void;

  // Agents
  agents: AgentInfo[];
  setAgentStatus: (agentId: string, status: AgentStatus) => void;
  registerAgent: (agent: AgentInfo) => void;

  // Active tasks
  activeTaskIds: string[];
  addActiveTask: (taskId: string) => void;
  removeActiveTask: (taskId: string) => void;

  // Input
  inputValue: string;
  setInputValue: (value: string) => void;
  mentionedAgentIds: string[];
  setMentionedAgents: (ids: string[]) => void;
}

export const useChatStore = create<ChatState>()(
  devtools(
    immer((set) => ({
      // Initial state
      messages: [],
      agents: [],
      activeTaskIds: [],
      inputValue: '',
      mentionedAgentIds: [],

      // Message actions
      addMessage: (message) =>
        set((state) => {
          state.messages.push({
            ...message,
            id: crypto.randomUUID(),
            timestamp: new Date(),
          });
        }),

      updateMessage: (id, updates) =>
        set((state) => {
          const msg = state.messages.find((m) => m.id === id);
          if (msg) Object.assign(msg, updates);
        }),

      appendToMessage: (id, content) =>
        set((state) => {
          const msg = state.messages.find((m) => m.id === id);
          if (msg) msg.content += content;
        }),

      setMessageStatus: (id, status) =>
        set((state) => {
          const msg = state.messages.find((m) => m.id === id);
          if (msg) msg.status = status;
        }),

      // Agent actions
      setAgentStatus: (agentId, status) =>
        set((state) => {
          const agent = state.agents.find((a) => a.id === agentId);
          if (agent) agent.status = status;
        }),

      registerAgent: (agent) =>
        set((state) => {
          if (!state.agents.find((a) => a.id === agent.id)) {
            state.agents.push(agent);
          }
        }),

      // Task actions
      addActiveTask: (taskId) =>
        set((state) => {
          if (!state.activeTaskIds.includes(taskId)) {
            state.activeTaskIds.push(taskId);
          }
        }),

      removeActiveTask: (taskId) =>
        set((state) => {
          state.activeTaskIds = state.activeTaskIds.filter((id) => id !== taskId);
        }),

      // Input actions
      setInputValue: (value) =>
        set((state) => {
          state.inputValue = value;
        }),

      setMentionedAgents: (ids) =>
        set((state) => {
          state.mentionedAgentIds = ids;
        }),
    })),
    { name: 'ChatStore' }
  )
);
```

### 5.2 React Context for SSE Connection

```tsx
// contexts/AgentConnectionContext.tsx
import React, { createContext, useContext, useCallback } from 'react';

interface AgentConnectionContextType {
  sendToAgent: (agentId: string, message: string) => Promise<void>;
  subscribeToAgent: (agentId: string, callback: (update: any) => void) => () => void;
  connectionStatus: Record<string, 'connected' | 'disconnected' | 'connecting'>;
}

const AgentConnectionContext = createContext<AgentConnectionContextType | null>(null);

export function AgentConnectionProvider({ children }: { children: React.ReactNode }) {
  const sendToAgent = useCallback(async (agentId: string, message: string) => {
    const response = await fetch(`/api/agents/${agentId}/tasks/send`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        id: crypto.randomUUID(),
        message: { role: 'user', parts: [{ type: 'text', text: message }] },
      }),
    });
    return response.json();
  }, []);

  return (
    <AgentConnectionContext.Provider value={{ sendToAgent, subscribeToAgent, connectionStatus }}>
      {children}
    </AgentConnectionContext.Provider>
  );
}
```

---

## 6. API Design: SignalR vs SSE vs WebSockets

### 6.1 Decision Framework

From [^597^], the pragmatic approach:

| Protocol | Direction | Best For | Agent Chat Use |
|----------|-----------|----------|----------------|
| **REST** | Client → Server | CRUD, auth, file uploads | Sending messages, fetching history |
| **SSE** | Server → Client | Streaming responses, notifications | Agent response streaming, status updates |
| **WebSockets** | Bidirectional | Real-time chat, games | Full-duplex agent chat (rarely needed) |
| **SignalR** | Bidirectional (fallback) | .NET chat apps | If using .NET backend with complex features |

### 6.2 Why SSE + REST is Best for A2A Agent Chat

A2A protocol is designed around **SSE for streaming** + **HTTP/JSON-RPC for commands** [^10^] [^120^]:

```
Client                                    Server
  |                                         |
  |--- POST /tasks/send ------------------->|  (Send task - REST)
  |<-- { taskId, status: "submitted" }------|  (JSON response)
  |                                         |
  |--- POST /tasks/sendSubscribe --------->|  (Start streaming)
  |<-- SSE: event: status ------------------|  (SSE stream)
  |    data: { state: "working", ... }      |
  |<-- SSE: event: status ------------------|
  |    data: { state: "working", ... }      |
  |<-- SSE: event: artifact ----------------|
  |    data: { parts: [...] }               |
  |<-- SSE: event: status ------------------|
  |    data: { state: "completed", final: true } |
  |                                         |
  |--- POST /tasks/cancel ----------------->|  (Cancel - REST)
  |--- GET  /tasks/get?id=xxx ------------->|  (Query status - REST)
```

### 6.3 SignalR Option (.NET Backend)

If using .NET with SignalR [^518^] [^519^]:

```csharp
// ChatHub.cs
using Microsoft.AspNetCore.SignalR;

public class AgentChatHub : Hub
{
    public async Task SendMessage(string agentId, string message)
    {
        await Clients.Group(agentId).SendAsync("ReceiveMessage", Context.UserIdentifier, message);
    }

    public async Task JoinAgentChannel(string agentId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, agentId);
    }

    public async Task SendAgentStatus(string agentId, string status)
    {
        await Clients.Group(agentId).SendAsync("AgentStatusUpdate", agentId, status);
    }
}
```

```tsx
// React SignalR client
import { useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder } from '@microsoft/signalr';

function useSignalRAgentChat(hubUrl: string) {
  const connection = useRef<any>(null);
  const [messages, setMessages] = useState<any[]>([]);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.current = conn;

    conn.on('ReceiveMessage', (user, message) => {
      setMessages((prev) => [...prev, { user, message }]);
    });

    conn.on('AgentStatusUpdate', (agentId, status) => {
      // Handle agent status updates
    });

    conn.start().then(() => setConnected(true));

    return () => conn.stop();
  }, [hubUrl]);

  const sendMessage = async (agentId: string, message: string) => {
    await connection.current?.invoke('SendMessage', agentId, message);
  };

  return { messages, connected, sendMessage };
}
```

### 6.4 Vercel AI SDK (Recommended for Quick Start)

The Vercel AI SDK abstracts streaming entirely [^550^] [^551^]:

```tsx
// Client
'use client';
import { useChat } from '@ai-sdk/react';

function ChatComponent() {
  const { messages, input, handleInputChange, handleSubmit, isLoading, stop } = useChat({
    api: '/api/chat',
    onFinish: (message) => {
      console.log('Stream complete:', message);
    },
  });

  return (
    <div>
      {messages.map((msg) => (
        <div key={msg.id} className={msg.role}>
          {msg.content}
        </div>
      ))}
      {isLoading && <AgentTypingIndicator agentName="AI" status="typing" />}
      <form onSubmit={handleSubmit}>
        <input value={input} onChange={handleInputChange} />
        <button type="submit">Send</button>
        {isLoading && <button onClick={stop}>Stop</button>}
      </form>
    </div>
  );
}
```

---

## 7. Agent Status Display

### 7.1 Status Indicator Component

A2A defines 9 task states [^603^] that map to UI statuses:

| A2A State | UI Status | Visual |
|-----------|-----------|--------|
| `submitted` | `running` | Yellow spinner |
| `working` | `running` | Animated dots |
| `completed` | `complete` | Green checkmark |
| `failed` | `error` | Red X |
| `canceled` | `cancelled` | Gray stop |
| `input_required` | `requires-action` | Blue question mark |

```tsx
// components/AgentStatusBadge.tsx
import { useChatStore, AgentStatus } from '../stores/chatStore';

const statusConfig: Record<AgentStatus, { color: string; icon: string; label: string }> = {
  idle: { color: 'gray', icon: '●', label: 'Idle' },
  working: { color: 'blue', icon: '◌', label: 'Working' },
  completed: { color: 'green', icon: '✓', label: 'Done' },
  error: { color: 'red', icon: '✗', label: 'Error' },
  input_required: { color: 'amber', icon: '?', label: 'Needs Input' },
};

export function AgentStatusBadge({ agentId }: { agentId: string }) {
  const agent = useChatStore((state) =>
    state.agents.find((a) => a.id === agentId)
  );

  if (!agent) return null;

  const config = statusConfig[agent.status];

  return (
    <div className={`flex items-center gap-1.5 text-${config.color}-600`}>
      <span className={agent.status === 'working' ? 'animate-spin' : ''}>
        {config.icon}
      </span>
      <span className="text-xs font-medium">{agent.name}</span>
      <span className="text-xs opacity-70">({config.label})</span>
    </div>
  );
}
```

### 7.2 Multi-Agent Status Panel

```tsx
// components/AgentStatusPanel.tsx
import { useChatStore } from '../stores/chatStore';

export function AgentStatusPanel() {
  const agents = useChatStore((state) => state.agents);

  return (
    <div className="agent-panel border-r w-64 p-4 space-y-3">
      <h3 className="font-semibold text-sm uppercase tracking-wide text-gray-500">
        Active Agents
      </h3>
      {agents.map((agent) => (
        <div key={agent.id} className="flex items-center gap-2 p-2 rounded hover:bg-gray-50">
          <div className="relative">
            <img src={agent.avatar} className="w-8 h-8 rounded-full" />
            <div
              className={`absolute -bottom-0.5 -right-0.5 w-3 h-3 rounded-full border-2 border-white ${
                agent.status === 'working'
                  ? 'bg-blue-400 animate-pulse'
                  : agent.status === 'completed'
                  ? 'bg-green-400'
                  : agent.status === 'error'
                  ? 'bg-red-400'
                  : 'bg-gray-300'
              }`}
            />
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-sm font-medium truncate">{agent.name}</div>
            <div className="text-xs text-gray-500">{agent.description}</div>
          </div>
        </div>
      ))}
    </div>
  );
}
```

---

## 8. Markdown & Code Block Rendering

### 8.1 react-markdown + react-syntax-highlighter

The standard combination from [^543^] [^520^]:

```bash
npm install react-markdown react-syntax-highlighter
npm install -D @types/react-syntax-highlighter
npm install remark-gfm  # GitHub Flavored Markdown
```

```tsx
import ReactMarkdown from 'react-markdown';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { oneDark } from 'react-syntax-highlighter/dist/cjs/styles/prism';
import remarkGfm from 'remark-gfm';

interface MarkdownMessageProps {
  content: string;
}

export function MarkdownMessage({ content }: MarkdownMessageProps) {
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      components={{
        code({ node, inline, className, children, ...props }: any) {
          const match = /language-(\w+)/.exec(className || '');
          return !inline && match ? (
            <div className="relative group">
              <div className="flex justify-between items-center px-3 py-1 bg-gray-800 text-gray-300 text-xs rounded-t">
                <span>{match[1]}</span>
                <button
                  onClick={() => navigator.clipboard.writeText(String(children))}
                  className="hover:text-white"
                >
                  Copy
                </button>
              </div>
              <SyntaxHighlighter
                style={oneDark}
                language={match[1]}
                PreTag="div"
                {...props}
              >
                {String(children).replace(/\n$/, '')}
              </SyntaxHighlighter>
            </div>
          ) : (
            <code className="bg-gray-100 px-1 py-0.5 rounded text-sm" {...props}>
              {children}
            </code>
          );
        },
        table({ children }) {
          return (
            <div className="overflow-x-auto">
              <table className="min-w-full border-collapse border border-gray-300">
                {children}
              </table>
            </div>
          );
        },
      }}
    >
      {content}
    </ReactMarkdown>
  );
}
```

### 8.2 MUI X Chat Code Block

For MUI X users, `ChatCodeBlock` handles code rendering automatically [^529^]:

```tsx
import { ChatCodeBlock } from '@mui/x-chat';
import { codeToHtml } from 'shiki';

function ShikiBlock({ code, language }: { code: string; language: string }) {
  const [html, setHtml] = React.useState('');

  React.useEffect(() => {
    codeToHtml(code, { lang: language, theme: 'github-light' }).then(setHtml);
  }, [code, language]);

  return (
    <ChatCodeBlock
      language={language}
      highlighter={() => <span dangerouslySetInnerHTML={{ __html: html }} />}
    >
      {code}
    </ChatCodeBlock>
  );
}
```

---

## 9. File Attachments

### 9.1 Upload Component

From [^533^] and [^537^], a file upload with progress:

```tsx
import { useState, useRef, useCallback } from 'react';

interface FileAttachment {
  file: File;
  id: string;
  progress: number;
  url?: string;
  error?: string;
}

function useFileUpload(maxSize: number = 10 * 1024 * 1024) {
  const [attachments, setAttachments] = useState<FileAttachment[]>([]);
  const abortControllers = useRef<Map<string, AbortController>>(new Map());

  const addFiles = useCallback(
    (files: FileList) => {
      const newFiles: FileAttachment[] = Array.from(files)
        .filter((f) => f.size <= maxSize)
        .map((f) => ({
          file: f,
          id: crypto.randomUUID(),
          progress: 0,
        }));
      setAttachments((prev) => [...prev, ...newFiles]);
      return newFiles;
    },
    [maxSize]
  );

  const uploadFile = useCallback(async (
    attachmentId: string,
    uploadUrl: string
  ) => {
    const attachment = attachments.find((a) => a.id === attachmentId);
    if (!attachment) return;

    const controller = new AbortController();
    abortControllers.current.set(attachmentId, controller);

    try {
      const formData = new FormData();
      formData.append('file', attachment.file);

      const response = await fetch(uploadUrl, {
        method: 'POST',
        body: formData,
        signal: controller.signal,
      });

      if (!response.ok) throw new Error('Upload failed');

      const result = await response.json();
      setAttachments((prev) =>
        prev.map((a) => (a.id === attachmentId ? { ...a, url: result.url, progress: 100 } : a))
      );
      return result.url;
    } catch (error: any) {
      setAttachments((prev) =>
        prev.map((a) => (a.id === attachmentId ? { ...a, error: error.message } : a))
      );
    }
  }, [attachments]);

  const removeAttachment = useCallback((id: string) => {
    abortControllers.current.get(id)?.abort();
    abortControllers.current.delete(id);
    setAttachments((prev) => prev.filter((a) => a.id !== id));
  }, []);

  return { attachments, addFiles, uploadFile, removeAttachment };
}
```

### 9.2 MUI X Chat Attachments

For MUI X users [^534^]:

```tsx
import { ChatBox } from '@mui/x-chat';

<ChatBox
  adapter={adapter}
  features={{
    attachments: {
      acceptedMimeTypes: ['image/*', 'application/pdf', 'text/plain'],
      maxFileCount: 5,
      maxFileSize: 10 * 1024 * 1024, // 10 MB
      onAttachmentReject: (rejections) => {
        rejections.forEach(({ file, reason }) => {
          console.warn(`Rejected ${file.name}: ${reason}`);
        });
      },
    },
  }}
/>
```

---

## 10. Multi-Agent Conversation UI Patterns

### 10.1 UI Pattern: Role Cards for Multiple Agents

From [^514^], the recommended pattern is **Role Cards** showing each agent's role, scope, and active status:

```tsx
// components/MultiAgentChat.tsx
import { useChatStore } from '../stores/chatStore';

export function MultiAgentChat() {
  const messages = useChatStore((state) => state.messages);
  const agents = useChatStore((state) => state.agents);
  const activeTaskIds = useChatStore((state) => state.activeTaskIds);

  return (
    <div className="flex h-screen">
      {/* Agent sidebar */}
      <AgentStatusPanel />

      {/* Main chat area */}
      <div className="flex-1 flex flex-col">
        {/* Active tasks bar */}
        {activeTaskIds.length > 0 && (
          <div className="bg-blue-50 px-4 py-2 flex gap-2 items-center">
            <span className="text-sm font-medium text-blue-700">
              Active tasks:
            </span>
            {activeTaskIds.map((taskId) => (
              <span key={taskId} className="text-xs bg-blue-100 text-blue-700 px-2 py-0.5 rounded-full">
                {taskId.slice(0, 8)}...
              </span>
            ))}
          </div>
        )}

        {/* Messages */}
        <div className="flex-1 overflow-y-auto p-4 space-y-4">
          {messages.map((msg) => (
            <div
              key={msg.id}
              className={`flex gap-3 ${msg.role === 'user' ? 'justify-end' : ''}`}
            >
              {msg.role === 'agent' && msg.agentName && (
                <div className="flex-shrink-0">
                  <div className="w-8 h-8 rounded-full bg-blue-500 flex items-center justify-center text-white text-xs">
                    {msg.agentName[0]}
                  </div>
                  <div className="text-xs text-gray-500 mt-1 text-center">
                    {msg.agentName}
                  </div>
                </div>
              )}
              <div
                className={`max-w-3xl rounded-lg p-3 ${
                  msg.role === 'user'
                    ? 'bg-blue-600 text-white'
                    : 'bg-gray-100'
                }`}
              >
                {msg.isStreaming ? (
                  <StreamingMessage content={msg.content} />
                ) : (
                  <MarkdownMessage content={msg.content} />
                )}
                {msg.status === 'working' && (
                  <AgentTypingIndicator
                    agentName={msg.agentName || 'Agent'}
                    status="working"
                  />
                )}
              </div>
            </div>
          ))}
        </div>

        {/* Input area */}
        <div className="border-t p-4">
          <AgentMentionInput
            agents={agents.map((a) => ({
              id: a.id,
              display: a.name,
              avatar: a.avatar,
              description: a.description,
            }))}
            onSubmit={handleSendMessage}
          />
        </div>
      </div>
    </div>
  );
}
```

### 10.2 Thread-Based Multi-Agent Pattern

From [^516^], implementing multi-agent threading:

```tsx
// Each agent response gets its own visual thread
interface AgentThread {
  id: string;
  agentId: string;
  agentName: string;
  parentMessageId: string;
  messages: ChatMessage[];
  status: 'active' | 'completed' | 'error';
}

function AgentThreadPanel({ thread }: { thread: AgentThread }) {
  return (
    <div className="border rounded-lg p-3 bg-gray-50">
      <div className="flex items-center gap-2 mb-2">
        <div className="w-6 h-6 rounded-full bg-blue-500 text-white text-xs flex items-center justify-center">
          {thread.agentName[0]}
        </div>
        <span className="text-sm font-medium">{thread.agentName}</span>
        <span
          className={`text-xs px-1.5 py-0.5 rounded ${
            thread.status === 'active'
              ? 'bg-yellow-100 text-yellow-700'
              : thread.status === 'completed'
              ? 'bg-green-100 text-green-700'
              : 'bg-red-100 text-red-700'
          }`}
        >
          {thread.status}
        </span>
      </div>
      <div className="space-y-1">
        {thread.messages.map((msg) => (
          <div key={msg.id} className="text-sm">
            {msg.content}
          </div>
        ))}
      </div>
    </div>
  );
}
```

---

## 11. Integration with A2A Protocol

### 11.1 Using Assistant-UI A2A Runtime

Assistant-UI has first-class A2A support [^603^]:

```tsx
import { useA2ARuntime } from '@assistant-ui/react-a2a';

function A2AChatComponent() {
  const runtime = useA2ARuntime({
    baseUrl: 'http://localhost:9999',
    onArtifactComplete: (artifact) => {
      console.log('Artifact ready:', artifact.name);
    },
    onError: (error) => {
      console.error('A2A Error:', error);
    },
  });

  return (
    <Thread runtime={runtime}>
      {/* Your chat UI */}
    </Thread>
  );
}

// Access agent card
function AgentInfo() {
  const card = useA2AAgentCard();

  if (!card) return null;

  return (
    <div>
      <h3>{card.name}</h3>
      <p>{card.description}</p>
      <div>Skills: {card.skills.map((s) => s.name).join(', ')}</div>
    </div>
  );
}

// Access artifacts
function ArtifactsPanel() {
  const artifacts = useA2AArtifacts();

  return (
    <div>
      {artifacts.map((artifact) => (
        <div key={artifact.name}>{artifact.name}</div>
      ))}
    </div>
  );
}
```

### 11.2 A2A Client Implementation

```typescript
// A2A client for React apps
class A2AChatClient {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
  }

  async discoverAgent(): Promise<AgentCard> {
    const response = await fetch(`${this.baseUrl}/.well-known/agent.json`);
    return response.json();
  }

  async sendTask(message: string, taskId: string = crypto.randomUUID()): Promise<Task> {
    const response = await fetch(`${this.baseUrl}/tasks/send`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        jsonrpc: '2.0',
        id: taskId,
        method: 'tasks/send',
        params: {
          id: taskId,
          message: { role: 'user', parts: [{ type: 'text', text: message }] },
        },
      }),
    });
    const result = await response.json();
    return result.result;
  }

  // SSE streaming
  streamTask(message: string, onUpdate: (update: any) => void): () => void {
    const taskId = crypto.randomUUID();
    const eventSource = new EventSource(
      `${this.baseUrl}/tasks/sendSubscribe?` +
        new URLSearchParams({
          taskId,
          message: JSON.stringify({ role: 'user', parts: [{ type: 'text', text: message }] }),
        })
    );

    eventSource.addEventListener('status', (event) => {
      const data = JSON.parse(event.data);
      onUpdate({ type: 'status', data: data.result });
    });

    eventSource.addEventListener('artifact', (event) => {
      const data = JSON.parse(event.data);
      onUpdate({ type: 'artifact', data: data.result });
    });

    eventSource.onerror = () => {
      eventSource.close();
      onUpdate({ type: 'error' });
    };

    return () => eventSource.close();
  }
}
```

---

## 12. Limitations & Gotchas

1. **SSE Connection Limits**: Browsers cap concurrent `EventSource` connections per domain (typically 6 over HTTP/1.1) [^597^]. Use HTTP/2 or multiplex via named events.

2. **Authentication**: Native `EventSource` doesn't support custom headers. Use cookies with `withCredentials`, query params, or the Fetch API-based approach for Bearer tokens [^515^].

3. **A2A Streaming State**: SSE connections don't resume from mid-stream. Use continuation tokens for reconnection [^630^].

4. **Zustand Selector Gotchas**: Using functions as selectors causes infinite loops. Use `useShallow` for multiple values [^621^] [^624^].

5. **Markdown Re-renders**: Parsing markdown on every token is expensive. Debounce or only parse after word boundaries [^520^].

6. **File Upload**: Native FileList is not a true array - destructure to `[...files]` before iterating [^533^].

7. **Agent Discovery**: Agent cards must be served from `/.well-known/agent.json` with proper CORS headers [^76^].

8. **No Binary Data in SSE**: SSE is text-only. Encode binary files as base64 [^597^].

---

## 13. Recommendations for Prototype

### Minimal Viable Architecture

```
Frontend (React + Vite)
├── @assistant-ui/react (chat UI primitives)
├── react-mentions (@-mention routing)
├── react-markdown + react-syntax-highlighter (rendering)
├── zustand (state management)
└── EventSource API (SSE streaming)

Backend (Node.js/Express or ASP.NET Core)
├── A2A-compatible endpoints
│   ├── GET  /.well-known/agent.json (Agent Card)
│   ├── POST /tasks/send (non-streaming)
│   ├── POST /tasks/sendSubscribe (SSE streaming)
│   ├── GET  /tasks/get (status query)
│   └── POST /tasks/cancel
├── SSE endpoint with proper headers
└── Agent routing logic
```

### Quick-Start Implementation Order

1. **Day 1**: Set up Assistant-UI with a mock chat endpoint
2. **Day 2**: Add SSE streaming with `EventSource`
3. **Day 3**: Integrate `react-mentions` for agent routing
4. **Day 4**: Add markdown rendering with code blocks
5. **Day 5**: Implement agent status indicators and sidebar
6. **Day 6**: Add file attachment support
7. **Day 7**: Wire up A2A protocol endpoints

### Key Packages Summary

| Package | Version | Purpose |
|---------|---------|---------|
| `@assistant-ui/react` | latest | Chat UI primitives |
| `react-mentions` | ^4.4.10 | @-mention system |
| `react-markdown` | ^9.0.0 | Markdown rendering |
| `react-syntax-highlighter` | ^15.5.0 | Code syntax highlighting |
| `remark-gfm` | ^4.0.0 | GitHub Flavored Markdown |
| `zustand` | ^4.5.0 | State management |
| `@reactuse/core` | latest | `useEventSource` hook |
| `ai` | ^4.0.0 | Vercel AI SDK (optional) |
| `@ai-sdk/react` | ^1.0.0 | `useChat` hook (optional) |

---

## 14. Sources & References

[^524^]: https://www.saastr.com/ai-app-of-the-week-assistant-ui-the-react-library-thats-eating-the-ai-chat-interface-market/ - Assistant-UI overview and features

[^515^]: https://oneuptime.com/blog/post/2026-01-15-server-sent-events-sse-react/view - Comprehensive SSE in React guide

[^510^]: https://medium.com/@dlrnjstjs/implementing-react-sse-server-sent-events-real-time-notification-system-a999bb983d1b - React SSE implementation

[^520^]: https://docs.langchain.com/oss/javascript/langchain/frontend/markdown-messages - Markdown rendering in chat

[^543^]: https://amirardalan.com/blog/syntax-highlight-code-in-markdown - react-markdown + react-syntax-highlighter setup

[^529^]: https://mui.com/x/react-chat/material/examples/code-block/ - MUI X Chat code block

[^538^]: https://stackoverflow.com/questions/77714002/how-can-i-use-react-mentions - react-mentions usage

[^539^]: https://primereact.org/mention/ - PrimeReact Mention component

[^534^]: https://mui.com/x/react-chat/behavior/attachments/ - MUI X Chat attachments

[^524^]: https://www.saastr.com/ai-app-of-the-week-assistant-ui-the-react-library-thats-eating-the-ai-chat-interface-market/ - Assistant-UI deep dive

[^603^]: https://www.assistant-ui.com/docs/runtimes/a2a - Assistant-UI A2A runtime

[^554^]: https://www.assistant-ui.com/docs/installation - Assistant-UI installation

[^550^]: https://www.digitalapplied.com/blog/vercel-ai-sdk-6-streaming-chat-nextjs-guide - Vercel AI SDK 6 streaming

[^551^]: https://www.9.agency/blog/streaming-ai-responses-vercel-ai-sdk - Vercel AI SDK streaming patterns

[^553^]: https://ai-sdk.dev/docs/ai-sdk-ui/streaming-data - AI SDK streaming custom data

[^518^]: https://www.dotnetdevelopers.us/blogs/dot-net-apps/ - SignalR for .NET real-time chat

[^519^]: https://www.geeksforgeeks.org/c-sharp/building-a-real-time-chat-application-with-net-core-7-and-signalr/ - SignalR + .NET Core chat

[^597^]: https://listiak.dev/blog/rest-vs-websockets-vs-sse-choosing-the-right-communication-pattern - REST vs WebSockets vs SSE comparison

[^600^]: https://medium.com/@sulmanahmed135/websockets-vs-server-sent-events-sse-a-practical-guide-for-real-time-data-streaming-in-modern-c57037a5a589 - WebSockets vs SSE practical guide

[^76^]: https://codilime.com/blog/a2a-protocol-explained/ - A2A protocol explained

[^10^]: https://www.ibm.com/think/topics/agent2agent-protocol - A2A protocol overview (IBM)

[^120^]: https://medium.com/google-cloud/a2a-deep-dive-getting-real-time-updates-from-ai-agents-a28d60317332 - A2A streaming deep dive

[^602^]: https://agent2agent.info/docs/topics/streaming-and-async/ - A2A streaming and async docs

[^531^]: https://www.cybage.com/blog/mastering-google-s-a2a-protocol-the-complete-guide-to-agent-to-agent-communication - A2A complete guide

[^514^]: https://hatchworks.com/blog/ai-agents/agent-ux-patterns/ - Agent UX patterns

[^516^]: https://www.reddit.com/r/AI_Agents/comments/1jqvdb1/emergent_ux_patterns_from_the_top_agent_builders/ - Multi-agent UI patterns

[^564^]: https://www.cometchat.com/tutorials/react-chat-typing-indicator - React typing indicator

[^545^]: https://macarthur.me/posts/streaming-text-with-typeit - Streaming text animation

[^562^]: https://dev.to/shrinivasshah/context-vs-zustand-vs-redux-a-a-senior-engineers-story-2jnm - Zustand vs Redux comparison

[^559^]: https://reactuse.com/browser/useeventsource/ - useEventSource hook

[^598^]: https://github.com/modelcontextprotocol/use-mcp - use-mcp React hook

[^604^]: https://www.premieroctet.com/blog/en/integration-mcp-dans-une-app-react - MCP React integration

[^609^]: https://github.com/copilotkit/copilotkit - CopilotKit GitHub

[^620^]: https://shadcnstudio.com/blog/shadcn-chat-ui-example - Shadcn chat UI examples

[^622^]: https://allshadcn.com/tools/shadcn-chat/ - Shadcn Chat components

[^611^]: https://libraries.io/npm/@mui%2Fx-chat - MUI X Chat npm

[^533^]: https://uploadcare.com/blog/how-to-upload-file-in-react/ - React file upload examples

[^537^]: https://angelhodar.com/blog/improve-your-file-uploads-in-react - React file upload improvements

[^517^]: https://www.langchain.com/blog/choosing-the-right-multi-agent-architecture - Multi-agent architecture patterns

[^518^]: https://www.dotnetdevelopers.us/blogs/dot-net-apps/ - SignalR .NET tutorial

[^521^]: https://medium.com/@mina.abdo/real-time-magic-with-signalr-in-net-a-step-by-step-guide-bdcf228995b1 - SignalR step-by-step

[^527^]: https://dev.to/morteza-jangjoo/build-a-real-time-chat-room-with-net-core-signalr-9ee - SignalR chat room

[^345^]: https://devblogs.microsoft.com/agent-framework/a2a-v1-is-here-cross-platform-agent-communication-in-microsoft-agent-framework-for-net/ - A2A v1 in Microsoft Agent Framework

[^598^]: https://github.com/modelcontextprotocol/use-mcp - use-mcp library

[^605^]: https://dev.to/copilotkit/turn-your-react-app-into-an-mcp-client-in-minutes-269n - MCP client in React

[^606^]: https://www.copilotkit.ai/blog/add-an-mcp-client-to-any-react-app-in-under-30-minutes - MCP client integration

[^607^]: https://blog.cloudflare.com/connect-any-react-application-to-an-mcp-server-in-three-lines-of-code/ - Connect React to MCP

[^343^]: https://learn.microsoft.com/en-us/agent-framework/integrations/a2a - A2A integration Microsoft docs

[^310^]: https://learn.microsoft.com/en-us/agent-framework/agents/providers/agent-to-agent - A2A Agent provider

[^630^]: https://learn.microsoft.com/zh-cn/agent-framework/agents/providers/agent-to-agent - A2A streaming reconnection

[^517^]: https://www.langchain.com/blog/choosing-the-right-multi-agent-architecture - Multi-agent architecture

[^516^]: https://www.reddit.com/r/AI_Agents/comments/1jqvdb1/emergent_ux_patterns_from_the_top_agent_builders/ - Emergent UX patterns

[^524^]: https://www.saastr.com/ai-app-of-the-week-assistant-ui-the-react-library-thats-eating-the-ai-chat-interface-market/ - Assistant-UI review

[^525^]: https://www.metered.ca/blog/top-react-ui-libraries-for-2024/ - Top React UI libraries 2025

[^526^]: https://www.danielcorin.com/posts/2024/lm-streaming-with-sse/ - Language model streaming with SSE

[^523^]: https://dev.to/nir_tzezana_029370cba9093/streaming-live-data-to-your-reactjs-app-using-server-side-events-40am - SSE React data streaming

[^530^]: https://www.pubnub.com/docs/chat/components/react-native/file-upload - File upload patterns

[^535^]: https://getstream.io/chat/docs/sdk/react-native/ui-components/file-attachment/ - Stream file attachment

[^541^]: https://www.syncfusion.com/react-components/react-mention - Syncfusion mention component

[^544^]: https://blog.logrocket.com/build-react-comment-form-mention-functionality/ - react-mentions tutorial

[^552^]: https://www.assistant-ui.com/blog/2026-03-launch-week - Assistant-UI launch week (mobile, terminal, cloud)

[^555^]: https://www.assistant-ui.com/docs/runtimes/langgraph/quickstart - Assistant-UI LangGraph quickstart

[^557^]: https://github.com/vezlo/assistant-chat - Assistant chat widget

[^560^]: https://vercel.com/blog/vercel-ai-sdk-3-3 - Vercel AI SDK 3.3 features

[^561^]: https://www.reddit.com/r/nextjs/comments/1ges3hh/anyone_know_how_i_can_read_the_vercel_ai_sdk/ - Reading AI SDK streams

[^599^]: https://stackoverflow.com/questions/79808031/google-adk-a2a-sse - A2A SSE streaming

[^601^]: https://community.databricks.com/t5/technical-blog/how-to-deploy-agent-to-agent-a2a-protocol-on-databricks-apps-gt/ba-p/134213 - A2A deployment guide

[^610^]: https://lobehub.com/skills/pjt222-agent-almanac-implement-a2a-server - A2A server implementation

[^618^]: https://a2aprotocol.ai/docs/guide/a2a-typescript-guide - A2A TypeScript guide

[^621^]: https://stackoverflow.com/questions/79146532/how-to-store-lists-for-arbitrary-keys-in-zustand - Zustand list storage

[^624^]: https://oneuptime.com/blog/post/2026-01-15-react-native-zustand-state/view - Zustand state management

[^625^]: https://github.com/microsoft/agent-framework/issues/3310 - A2A streaming in .NET

[^628^]: https://cloud.tencent.com/developer/article/2665051 - A2A .NET ecosystem

[^620^]: https://shadcnstudio.com/blog/shadcn-chat-ui-example - Shadcn chat UI examples

[^623^]: https://github.com/shadcnblockscom/shadcn-ui-blocks - Shadcn UI blocks

[^627^]: https://blog.bitsrc.io/top-9-react-component-libraries-for-2025-a11139b3ed2e - React component libraries 2025
