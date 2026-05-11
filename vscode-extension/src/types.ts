/**
 * Type definitions for the Expert Agents VS Code extension.
 *
 * Contains all domain interfaces shared across the extension:
 * - ChatRequest / ChatResponse: backend protocol
 * - CodeReference: a code snippet returned by the agent
 * - AgentInfo: metadata for each expert agent
 * - Extension configuration keys
 */

/** ------------------------------------------------------------------ */
/**  Backend protocol                                                   */
/** ------------------------------------------------------------------ */

/**
 * Payload sent to the backend /api/chat endpoint.
 */
export interface ChatRequest {
  /** Which expert agent should handle the message. */
  agentId: string;
  /** The user's message text. */
  message: string;
  /** Session identifier for multi-turn context. */
  sessionId: string;
}

/**
 * Payload received from the backend /api/chat endpoint.
 */
export interface ChatResponse {
  /** Markdown-formatted response text. */
  response: string;
  /** The agent that produced the response. */
  agentId: string;
  /** Optional code references cited in the response. */
  references?: CodeReference[];
}

/**
 * A code snippet returned by an expert agent.
 */
export interface CodeReference {
  /** Relative or absolute file path. */
  filePath: string;
  /** First line of the range (1-based). */
  lineStart: number;
  /** Last line of the range (1-based). */
  lineEnd: number;
  /** The actual code content. */
  content: string;
}

/** ------------------------------------------------------------------ */
/**  Agent metadata                                                     */
/** ------------------------------------------------------------------ */

/**
 * Information about a registered expert agent.
 */
export interface AgentInfo {
  /** Unique machine-readable identifier (e.g. "fhir-server-expert"). */
  id: string;
  /** Human-readable name. */
  name: string;
  /** Longer description of the agent's expertise. */
  description: string;
}

/** ------------------------------------------------------------------ */
/**  Configuration                                                      */
/** ------------------------------------------------------------------ */

/**
 * Extension settings exposed through vscode.workspace.getConfiguration().
 */
export interface ExtensionConfig {
  /** Base URL of the Expert Agents backend service. */
  backendUrl: string;
  /** Whether streaming responses are enabled. */
  enableStreaming: boolean;
}

/** ------------------------------------------------------------------ */
/**  Internal helpers                                                   */
/** ------------------------------------------------------------------ */

/**
 * Maps a VS Code chat participant ID to the backend agentId.
 *
 * @param participantId - The VS Code participant ID (e.g. "expert-agents.fhir-server")
 * @returns The backend agent ID (e.g. "fhir-server-expert")
 */
export function participantIdToAgentId(participantId: string): string {
  switch (participantId) {
    case 'expert-agents.fhir-server':
      return 'fhir-server-expert';
    case 'expert-agents.healthcare-components':
      return 'healthcare-components-expert';
    default:
      return participantId;
  }
}
