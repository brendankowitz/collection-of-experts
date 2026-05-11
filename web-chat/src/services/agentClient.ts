import { useChatStore, type Agent, type Message } from '../store/chatStore';

const USE_MOCK = true;
const API_BASE = '/api';

// Mock response data for different agents
const mockResponses: Record<string, string[]> = {
  'fhir-server-expert': [
    `The **Microsoft FHIR Server for Azure** is an open-source implementation of the FHIR (Fast Healthcare Interoperability Resources) specification that runs on Azure.\n\n## Key Features\n\n- **FHIR R4 Support**: Full support for FHIR Release 4 specification\n- **RESTful API**: Standard FHIR REST API with JSON and XML formats\n- **SMART on FHIR**: OAuth2 integration for app launching and authorization\n- **Auditing**: Comprehensive audit logging for compliance\n- **Export**: Bulk FHIR data export capability\n\n## Architecture\n\n\`\`\`typescript\n// Example: Search for patients\nconst response = await fetch(\n  '/fhir/Patient?given=John&family=Doe',\n  {\n    headers: { 'Authorization': 'Bearer ' + token }\n  }\n);\nconst bundle = await response.json();\n\`\`\`\n\nThe server is built with **.NET Core** and uses **Cosmos DB** or **SQL Server** as the data store. It supports:\n\n1. Conditional create/update\n2. FHIR bundles\n3. Custom search parameters\n4. Reindexing operations`,

    `**SMART on FHIR** is a set of open specifications to integrate apps with Electronic Health Records (EHRs), portals, and other healthcare IT systems.\n\n## Launch Sequence\n\n1. **App Registration**: Register your app in Azure AD\n2. **Launch Context**: EHR launches the app with context\n3. **Authorization**: OAuth2 flow for token exchange\n4. **FHIR Access**: Use access token to call FHIR API\n\n## Scopes\n\n| Scope | Description |\n|-------|-------------|\n| \`patient/*.read\` | Read access to patient data |\n| \`patient/*.write\` | Write access to patient data |\n| \`launch\` | Request launch context |\n| \`openid\` | OpenID Connect authentication |\n| \`fhirUser\` | Access to user FHIR resource |`,

    `FHIR **Bundle** transactions allow you to submit multiple resources as a single atomic operation. This is critical for data integrity.\n\n## Transaction Types\n\n- **batch**: Collection of operations processed independently\n- **transaction**: All operations succeed or fail together\n\n## Example Bundle\n\n\`\`\`json\n{\n  \"resourceType\": \"Bundle\",\n  \"type\": \"transaction\",\n  \"entry\": [\n    {\n      \"fullUrl\": \"urn:uuid:patient-1\",\n      \"resource\": {\n        \"resourceType\": \"Patient\",\n        \"name\": [{ \"family\": \"Doe\", \"given\": [\"John\"] }]\n      },\n      \"request\": {\n        \"method\": \"POST\",\n        \"url\": \"Patient\"\n      }\n    },\n    {\n      \"fullUrl\": \"urn:uuid:observation-1\",\n      \"resource\": {\n        \"resourceType\": \"Observation\",\n        \"status\": \"final\",\n        \"code\": { \"text\": \"Heart Rate\" }\n      },\n      \"request\": {\n        \"method\": \"POST\",\n        \"url\": \"Observation\"\n      }\n    }\n  ]\n}\n\`\`\``,
  ],
  'healthcare-components-expert': [
    `The **Microsoft Healthcare Shared Components** are a collection of services and libraries designed to accelerate healthcare application development on Azure.\n\n## Component Overview\n\n| Component | Purpose |\n|-----------|---------|\n| **DICOM Service** | Store and manage medical imaging |\n| **IoT Connector** | Ingest device data to FHIR |\n| **FHIR Service** | Health data management |\n| **Identity** | Secure authentication |\n\n## DICOM Service\n\nThe DICOM (Digital Imaging and Communications in Medicine) service enables:\n\n- Store and query DICOM instances\n- WADO-RS for image retrieval\n- DICOMweb standard compliance\n- Integration with Azure AI for radiology insights\n\n\`\`\`python\nfrom azure.healthcare.dicom import DicomClient\n\nclient = DicomClient(endpoint, credential)\n\n# Store a DICOM instance\nwith open('scan.dcm', 'rb') as f:\n    result = client.store_instances(f)\n\n# Query studies\nstudies = client.query_studies(\n    patient_name='DOE*'\n)\n\`\`\``, 

    `The **IoT Connector** is a key component that bridges medical devices with FHIR. It transforms device data into FHIR Observations and other resources.\n\n## Data Flow\n\n\`\`\nMedical Device → IoT Hub → Device Mapping → FHIR Mapping → FHIR Service\n\`\`\n\n## Mapping Templates\n\nThe connector uses two types of mappings:\n\n1. **Device Mapping**: Normalizes incoming device data\n2. **FHIR Mapping**: Converts normalized data to FHIR resources\n\n## Example Device Mapping\n\n\`\`\`json\n{\n  \"templateType\": \"JsonPathContent\",\n  \"template\": {\n    \"typeName\": \"heartrate\",\n    \"values\": [\n      {\n        \"required\": \"true\",\n        \"valueExpression\": {\n          \"value\": \"$.heartrate\",\n          \"language\": \"JsonPath\"\n        }\n      }\n    ]\n  }\n}\n\`\`\`\n\n## Normalization\n\nThe normalization step extracts:\n- **Device ID**: Unique device identifier\n- **Type**: Measurement type (e.g., heart rate)\n- **Value**: Numeric measurement value\n- **Unit**: UCUM unit code\n- **Timestamp**: ISO 8601 datetime`,
  ],
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
  const words = fullResponse.split(/(\s+|[,.!?;:[\]{}()`"'])/).filter(Boolean);

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

  async sendMessage(
    agentId: string,
    message: string,
    sessionId: string
  ): Promise<string> {
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
            if (cancelled) break;
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

      stream();

      return () => {
        cancelled = true;
      };
    }

    // Real SSE implementation
    const controller = new AbortController();
    let eventSource: EventSource | null = null;

    const connect = () => {
      const params = new URLSearchParams({ agentId, message, sessionId });
      eventSource = new EventSource(`${this.baseUrl}/chat/stream?${params}`);

      eventSource.onmessage = (event) => {
        if (event.data === '[DONE]') {
          eventSource?.close();
          onComplete();
          return;
        }
        try {
          const data = JSON.parse(event.data);
          if (data.chunk) {
            onChunk(data.chunk);
          }
        } catch {
          onChunk(event.data);
        }
      };

      eventSource.onerror = () => {
        eventSource?.close();
        onError(new Error('Stream connection failed'));
      };
    };

    connect();

    return () => {
      controller.abort();
      eventSource?.close();
    };
  }

  async createMessage(
    agentId: string,
    content: string,
    sessionId: string
  ): Promise<{ message: Message; cancel: () => void }> {
    const store = useChatStore.getState();

    // Create user message
    const userMessage: Message = {
      id: `msg-${Date.now()}-${Math.random().toString(36).slice(2, 5)}`,
      role: 'user',
      content,
      timestamp: new Date(),
      status: 'complete',
    };

    store.addMessage(userMessage);

    // Create assistant message placeholder
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

    // Start streaming
    const cancel = this.streamMessage(
      agentId,
      content,
      sessionId,
      (chunk) => {
        store.updateMessage(assistantMessageId, {
          content: store.messages.find((m) => m.id === assistantMessageId)?.content + chunk,
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
