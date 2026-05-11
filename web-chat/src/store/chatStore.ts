import { create } from 'zustand';

export interface Message {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  agentId?: string;
  timestamp: Date;
  status?: 'sending' | 'streaming' | 'complete' | 'error';
}

export interface Agent {
  id: string;
  name: string;
  description: string;
  status: 'online' | 'offline' | 'busy';
  icon: string;
  color: string;
}

interface ChatStore {
  // State
  messages: Message[];
  agents: Agent[];
  selectedAgent: string | null;
  isStreaming: boolean;
  sessionId: string;
  isSidebarOpen: boolean;
  darkMode: boolean;

  // Actions
  addMessage: (msg: Message) => void;
  updateMessage: (id: string, updates: Partial<Message>) => void;
  setStreaming: (val: boolean) => void;
  selectAgent: (id: string) => void;
  clearMessages: () => void;
  toggleSidebar: () => void;
  toggleDarkMode: () => void;
  setAgents: (agents: Agent[]) => void;
  updateAgentStatus: (id: string, status: Agent['status']) => void;
}

const generateSessionId = () => `session-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

export const defaultAgents: Agent[] = [
  {
    id: 'fhir-server-expert',
    name: 'FHIR Server Expert',
    description: 'Expert in Microsoft FHIR Server for Azure - FHIR API, data management, SMART on FHIR, and healthcare data interoperability.',
    status: 'online',
    icon: 'database',
    color: 'fhir',
  },
  {
    id: 'healthcare-components-expert',
    name: 'Healthcare Components Expert',
    description: 'Expert in Microsoft Healthcare Shared Components - DICOM, IoT connector, identity, and health data services.',
    status: 'online',
    icon: 'activity',
    color: 'healthcare',
  },
  {
    id: 'system',
    name: 'System',
    description: 'System notifications and status updates',
    status: 'online',
    icon: 'settings',
    color: 'system',
  },
];

export const suggestedPrompts: Record<string, string[]> = {
  'fhir-server-expert': [
    'How do I set up the FHIR Server for Azure?',
    'What is SMART on FHIR and how does it work?',
    'Explain FHIR search parameters with examples',
    'How do I handle FHIR bundle transactions?',
    'What are the FHIR R4 resource types?',
  ],
  'healthcare-components-expert': [
    'What are the Healthcare Shared Components?',
    'How does the DICOM service work in Azure?',
    'Explain the IoT connector for healthcare data',
    'How do I integrate identity services?',
    'What are the deployment options for health data services?',
  ],
};

export const useChatStore = create<ChatStore>((set) => ({
  // Initial state
  messages: [],
  agents: defaultAgents,
  selectedAgent: 'fhir-server-expert',
  isStreaming: false,
  sessionId: generateSessionId(),
  isSidebarOpen: false,
  darkMode: true,

  // Actions
  addMessage: (msg) =>
    set((state) => ({
      messages: [...state.messages, msg],
    })),

  updateMessage: (id, updates) =>
    set((state) => ({
      messages: state.messages.map((msg) =>
        msg.id === id ? { ...msg, ...updates } : msg
      ),
    })),

  setStreaming: (val) => set({ isStreaming: val }),

  selectAgent: (id) => set({ selectedAgent: id, isSidebarOpen: false }),

  clearMessages: () =>
    set({ messages: [], sessionId: generateSessionId() }),

  toggleSidebar: () =>
    set((state) => ({ isSidebarOpen: !state.isSidebarOpen })),

  toggleDarkMode: () =>
    set((state) => ({ darkMode: !state.darkMode })),

  setAgents: (agents) => set({ agents }),

  updateAgentStatus: (id, status) =>
    set((state) => ({
      agents: state.agents.map((agent) =>
        agent.id === id ? { ...agent, status } : agent
      ),
    })),
}));
