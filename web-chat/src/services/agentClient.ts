import { useChatStore, type Agent, type Message } from '../store/chatStore';

const USE_MOCK = true;
const API_BASE = '/api';

const mockResponses: Record<string, string[]> = {
  'fhir-server-expert': [
    `The **Microsoft FHIR Server for Azure** is an open-source implementation of the FHIR (Fast Healthcare Interoperability Resources) specification that runs on Azure.

## Key Features

- **FHIR R4 Support**: Full support for FHIR Release 4 specification
- **RESTful API**: Standard FHIR REST API with JSON and XML formats
- **SMART on FHIR**: OAuth2 integration for app launching and authorization
- **Auditing**: Comprehensive audit logging for compliance
- **Export**: Bulk FHIR data export capability

## Architecture

\`\`\`typescript
// Example: Search for patients
const response = await fetch(
  '/fhir/Patient?given=John&family=Doe',
  {
    headers: { 'Authorization': 'Bearer ' + token }
  }
);
const bundle = await response.json();
\`\`\`

The server is built with **.NET Core** and uses **Cosmos DB** or **SQL Server** as the data store. It supports:

1. Conditional create/update
2. FHIR bundles
3. Custom search parameters
4. Reindexing operations`,
    `**SMART on FHIR** is a set of open specifications to integrate apps with Electronic Health Records (EHRs), portals, and other healthcare IT systems.

## Launch Sequence

1. **App Registration**: Register your app in Azure AD
2. **Launch Context**: EHR launches the app with context
3. **Authorization**: OAuth2 flow for token exchange
4. **FHIR Access**: Use access token to call FHIR API

## Scopes

| Scope | Description |
|-------|-------------|
| \`patient/*.read\` | Read access to patient data |
| \`patient/*.write\` | Write access to patient data |
| \`launch\` | Request launch context |
| \`openid\` | OpenID Connect authentication |
| \`fhirUser\` | Access to user FHIR resource |`,
    `FHIR **Bundle** transactions allow you to submit multiple resources as a single atomic operation. This is critical for data integrity.

## Transaction Types

- **batch**: Collection of operations processed independently
- **transaction**: All operations succeed or fail together

## Example Bundle

\`\`\`json
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "fullUrl": "urn:uuid:patient-1",
      "resource": {
        "resourceType": "Patient",
        "name": [{ "family": "Doe", "given": ["John"] }]
      },
      "request": {
        "method": "POST",
        "url": "Patient"
      }
    }
  ]
}
\`\`\``
  ],
  'healthcare-components-expert': [
    `The **Microsoft Healthcare Shared Components** are a collection of services and libraries designed to accelerate healthcare application development on Azure.

## Component Overview

| Component | Purpose |
|-----------|---------|
| **DICOM Service** | Store and manage medical imaging |
| **IoT Connector** | Ingest device data to FHIR |
| **FHIR Service** | Health data management |
| **Identity** | Secure authentication |`,
    `The **IoT Connector** bridges medical devices with FHIR. It transforms device data into FHIR Observations and other resources.

## Data Flow

\`\`\`
Medical Device → IoT Hub → Device Mapping → FHIR Mapping → FHIR Service
\`\`\`

The normalization step extracts device ID, type, value, unit, and timestamp.`
  ]
};

let mockIndex = 0;

function getNextMockResponse(agentId: string): string {
  const responses = mockResponses[agentId] || mockResponses['fhir-server-expert'];
  const response = responses[mockIndex % responses.length];
  mockIndex++;
  return response;
}

async function* mockStreamResponse(agentId: string): AsyncGenerator<string> {
  const fullResponse = getNextMockResponse(agentId);
  const words = fullResponse.split(/(\s+|[,.!?;:[\]{}()\`"'])/).filter(Boolean);

  for (const word of words) {
    await new Promise((resolve) => setTimeout(resolve, 80));
    yield word;
  }
}

class AgentClient {
  private baseUrl: string;

  constructor() {
    this.baseUrl = API_BASE;
  }

  async getAgents(): Promise<Agent[]> {
    if (USE_MOCK) {
      return useChatStore.getState().agents;
    }

    const response = await fetch(`${this.baseUrl}/agents`);
    if (!response.ok) {
      throw new Error(`Failed to fetch agents: ${response.statusText}`);
    }

    const data = await response.json();
    return data.agents;
  }

  async sendMessage(agentId: string, message: string, sessionId: string): Promise<string> {
    if (USE_MOCK) {
      await new Promise((resolve) => setTimeout(resolve, 1000));
      return getNextMockResponse(agentId);
    }

    const response = await fetch(`${this.baseUrl}/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ agentId, message, sessionId }),
    });

    if (!response.ok) {
      throw new Error(`Failed to send message: ${response.statusText}`);
    }

    const data = await response.json();
    return data.response;
  }

  streamMessage(
    agentId: string,
    message: string,
    sessionId: string,
    onChunk: (chunk: string) => void,
    onComplete: () => void,
    onError: (error: Error) => void
  ): () => void {
    if (USE_MOCK) {
      let cancelled = false;

      const stream = async () => {
        try {
          for await (const chunk of mockStreamResponse(agentId)) {
            if (cancelled) {
              break;
            }

            onChunk(chunk);
          }

          if (!cancelled) {
            onComplete();
          }
        } catch (err) {
          if (!cancelled) {
            onError(err instanceof Error ? err : new Error(String(err)));
          }
        }
      };

      void stream();

      return () => {
        cancelled = true;
      };
    }

    const controller = new AbortController();

    const connectSse = async () => {
      try {
        const response = await fetch('/tasks/sendSubscribe', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            agentId,
            sessionId,
            message: {
              role: 'user',
              parts: [{ type: 'text', text: message }],
            },
          }),
          signal: controller.signal,
        });

        if (!response.ok || !response.body) {
          onError(new Error(`Stream failed: ${response.statusText}`));
          return;
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
          const { done, value } = await reader.read();
          if (done) {
            break;
          }

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop() ?? '';

          for (const line of lines) {
            if (!line.startsWith('data:')) {
              continue;
            }

            const data = line.slice(5).trim();
            try {
              const evt = JSON.parse(data) as { event?: string; text?: string };
              if (evt.event === 'done') {
                onComplete();
                return;
              }

              if (evt.event === 'text' && evt.text) {
                onChunk(evt.text);
              }
            } catch {
              // Ignore malformed SSE payloads.
            }
          }
        }

        onComplete();
      } catch (err: unknown) {
        if ((err as { name?: string })?.name !== 'AbortError') {
          onError(err instanceof Error ? err : new Error(String(err)));
        }
      }
    };

    void connectSse();
    return () => controller.abort();
  }

  async createMessage(
    agentId: string,
    content: string,
    sessionId: string
  ): Promise<{ message: Message; cancel: () => void }> {
    const store = useChatStore.getState();

    const userMessage: Message = {
      id: `msg-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`,
      role: 'user',
      content,
      timestamp: new Date(),
      status: 'complete',
    };

    store.addMessage(userMessage);

    const assistantMessageId = `msg-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`;
    const assistantMessage: Message = {
      id: assistantMessageId,
      role: 'assistant',
      content: '',
      agentId,
      timestamp: new Date(),
      status: 'streaming',
    };

    store.addMessage(assistantMessage);
    store.setStreaming(true);

    const cancel = this.streamMessage(
      agentId,
      content,
      sessionId,
      (chunk) => {
        store.updateMessage(assistantMessageId, {
          content: `${store.messages.find((m) => m.id === assistantMessageId)?.content ?? ''}${chunk}`,
        });
      },
      () => {
        store.updateMessage(assistantMessageId, { status: 'complete' });
        store.setStreaming(false);
      },
      (error) => {
        store.updateMessage(assistantMessageId, {
          status: 'error',
          content: `Error: ${error.message}. Please try again.`,
        });
        store.setStreaming(false);
      }
    );

    return { message: assistantMessage, cancel };
  }
}

export const agentClient = new AgentClient();
export { USE_MOCK };
