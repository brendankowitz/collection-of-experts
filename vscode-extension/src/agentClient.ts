/**
 * HTTP client for communicating with the Expert Agents backend.
 *
 * Uses Node.js built-in `http` / `https` modules so the extension has **no**
 * external runtime dependencies.
 */

import * as http from 'node:http';
import * as https from 'node:https';
import { URL } from 'node:url';
import type { ChatRequest, ChatResponse, AgentInfo } from './types';

/**
 * Options for the AgentClient.
 */
export interface AgentClientOptions {
  /** Base URL of the backend (e.g. "http://localhost:5000"). */
  baseUrl: string;
  /** Request timeout in milliseconds. */
  timeoutMs?: number;
}

/**
 * Low-level error raised when the backend cannot be reached.
 */
export class AgentClientError extends Error {
  constructor(
    message: string,
    public readonly cause?: Error
  ) {
    super(message);
    this.name = 'AgentClientError';
  }
}

/**
 * HTTP client that talks to the Expert Agents backend.
 *
 * Example:
 * ```typescript
 * const client = new AgentClient({ baseUrl: 'http://localhost:5000' });
 * const reply = await client.sendMessage({
 *   agentId: 'fhir-server-expert',
 *   message: 'How does custom search work?',
 *   sessionId: 'abc-123',
 * });
 * ```
 */
export class AgentClient {
  private readonly baseUrl: string;
  private readonly timeoutMs: number;

  constructor(options: AgentClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/$/, '');
    this.timeoutMs = options.timeoutMs ?? 30000;
  }

  /* ────────────────────────── Public API ────────────────────────── */

  /**
   * Send a single-turn chat message and wait for the full response.
   *
   * @param request - Chat request payload
   * @returns Parsed chat response from the backend
   * @throws AgentClientError on network or HTTP failure
   */
  async sendMessage(request: ChatRequest): Promise<ChatResponse> {
    const payload = JSON.stringify(request);
    const responseBody = await this.post('/api/chat', payload);
    return JSON.parse(responseBody) as ChatResponse;
  }

  /**
   * Send a chat message and yield response chunks as they arrive.
   *
   * This simulates streaming by requesting the backend with an
   * `Accept: text/event-stream` header and parsing Server-Sent Events.
   * If the backend does not support SSE, the full response is yielded
   * as a single chunk.
   *
   * @param request - Chat request payload
   * @yields Response text chunks
   */
  async *streamMessage(request: ChatRequest): AsyncGenerator<string> {
    const payload = JSON.stringify(request);

    try {
      const responseBody = await this.post('/api/chat', payload, {
        'Accept': 'text/event-stream',
      });

      // Attempt SSE parsing
      const lines = responseBody.split('\n');
      let buffer = '';
      for (const line of lines) {
        const trimmed = line.trim();
        if (trimmed.startsWith('data:')) {
          const data = trimmed.slice(5).trim();
          if (data === '[DONE]') {
            if (buffer) {
              yield buffer;
            }
            return;
          }
          try {
            const parsed = JSON.parse(data);
            const chunk = typeof parsed === 'string' ? parsed : parsed.content ?? parsed.text ?? '';
            if (chunk) {
              buffer += chunk;
              yield chunk;
            }
          } catch {
            // Not JSON – treat as raw text
            buffer += data;
            yield data;
          }
        }
      }

      if (buffer) {
        yield buffer;
      } else {
        // Not SSE – yield the full body
        yield responseBody;
      }
    } catch (err) {
      // Fallback: try non-streaming endpoint
      const response = await this.sendMessage(request);
      yield response.response;
    }
  }

  /**
   * Fetch the list of available agents from the backend.
   *
   * @returns Array of agent metadata
   */
  async getAgents(): Promise<AgentInfo[]> {
    const body = await this.get('/api/agents');
    return JSON.parse(body) as AgentInfo[];
  }

  /* ──────────────────────── HTTP helpers ────────────────────────── */

  /**
   * Perform an HTTP POST request.
   */
  private post(
    path: string,
    body: string,
    extraHeaders?: Record<string, string>
  ): Promise<string> {
    return this.request('POST', path, body, extraHeaders);
  }

  /**
   * Perform an HTTP GET request.
   */
  private get(path: string): Promise<string> {
    return this.request('GET', path);
  }

  /**
   * Low-level HTTP request helper using only Node.js built-ins.
   */
  private request(
    method: 'GET' | 'POST',
    path: string,
    body?: string,
    extraHeaders?: Record<string, string>
  ): Promise<string> {
    return new Promise((resolve, reject) => {
      const url = new URL(path, this.baseUrl);
      const options: http.RequestOptions = {
        method,
        hostname: url.hostname,
        port: url.port || (url.protocol === 'https:' ? '443' : '80'),
        path: url.pathname + url.search,
        headers: {
          'Content-Type': 'application/json',
          ...(body ? { 'Content-Length': Buffer.byteLength(body) } : {}),
          ...extraHeaders,
        },
        timeout: this.timeoutMs,
      };

      const lib = url.protocol === 'https:' ? https : http;

      const req = lib.request(options, (res) => {
        let data = '';
        res.setEncoding('utf8');
        res.on('data', (chunk: string) => {
          data += chunk;
        });
        res.on('end', () => {
          const statusCode = res.statusCode ?? 0;
          if (statusCode >= 200 && statusCode < 300) {
            resolve(data);
          } else {
            reject(
              new AgentClientError(
                `Backend returned HTTP ${statusCode}: ${data.slice(0, 500)}`
              )
            );
          }
        });
      });

      req.on('error', (err: Error) => {
        reject(new AgentClientError(`Request failed: ${err.message}`, err));
      });

      req.on('timeout', () => {
        req.destroy();
        reject(new AgentClientError(`Request timed out after ${this.timeoutMs}ms`));
      });

      if (body) {
        req.write(body);
      }
      req.end();
    });
  }
}
