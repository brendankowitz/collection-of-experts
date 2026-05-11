/**
 * FHIR Server Expert chat participant.
 *
 * Handles chat requests for the `@fhir-server-expert` agent.
 * Delegates to the backend API when available, or provides rich
 * mock responses when the backend is unreachable.
 */

import * as vscode from 'vscode';
import { AgentClient } from './agentClient';
import type { ChatRequest } from './types';

/** The agent identifier used when calling the backend. */
const AGENT_ID = 'fhir-server-expert';

/** Fixed set of follow-up questions shown after each response. */
const FOLLOW_UP_QUESTIONS = [
  'How does custom search work?',
  'Explain the data access layer',
  'How do I create a PR?',
];

/**
 * The set of follow-up questions shown after each FHIR Server Expert response.
 */
export const fhirFollowUpQuestions: string[] = FOLLOW_UP_QUESTIONS;

/**
 * Create the VS Code ChatRequestHandler for the FHIR Server Expert.
 *
 * @param client - The HTTP client for talking to the backend
 * @returns A handler compatible with vscode.chat.createChatParticipant
 */
export function createFhirServerParticipantHandler(
  client: AgentClient
): vscode.ChatRequestHandler {
  return async (
    request: vscode.ChatRequest,
    context: vscode.ChatContext,
    stream: vscode.ChatResponseStream,
    token: vscode.CancellationToken
  ): Promise<vscode.ChatResult> => {
    const prompt = request.prompt.trim();
    if (!prompt) {
      stream.markdown('Hello! I am the **FHIR Server Expert**. Ask me anything about Microsoft FHIR Server architecture, code, or PR guidance.');
      return { metadata: { agentId: AGENT_ID, followups: FOLLOW_UP_QUESTIONS } };
    }

    // Show progress indicator
    stream.progress('Consulting the FHIR Server Expert...');

    // Derive a stable session ID from conversation history
    const sessionId = deriveSessionId(context);

    // Try the backend first; fall back to mock mode on failure
    try {
      const chatRequest: ChatRequest = {
        agentId: AGENT_ID,
        message: prompt,
        sessionId,
      };

      const config = vscode.workspace.getConfiguration('expertAgents');
      const streamingEnabled = config.get<boolean>('enableStreaming', true);

      if (streamingEnabled) {
        let fullResponse = '';
        for await (const chunk of client.streamMessage(chatRequest)) {
          // Check for cancellation
          if (token.isCancellationRequested) {
            stream.markdown('\n\n*Request cancelled.*');
            return { metadata: { cancelled: true } };
          }
          stream.markdown(chunk);
          fullResponse += chunk;
        }
      } else {
        const response = await client.sendMessage(chatRequest);
        stream.markdown(response.response);
      }
    } catch (err) {
      if (token.isCancellationRequested) {
        stream.markdown('\n\n*Request cancelled.*');
        return { metadata: { cancelled: true } };
      }
      // Backend unreachable – serve rich mock response
      const mockResponse = getMockResponse(prompt);
      stream.markdown(mockResponse);
    }

    return {
      metadata: { agentId: AGENT_ID },
    };
  };
}

/**
 * Derive a deterministic session ID from the conversation history.
 *
 * This keeps multi-turn context stable without external storage.
 */
function deriveSessionId(context: vscode.ChatContext): string {
  const history = context.history;
  if (history.length === 0) {
    return `fhir-${Date.now()}`;
  }
  // Simple hash of the first user prompt to keep the session stable
  const firstPrompt =
    history[0]?.participant === undefined
      ? String((history[0] as { prompt?: string })?.prompt ?? '')
      : '';
  return `fhir-${hashCode(firstPrompt || 'default')}`;
}

/** Simple string hash for deterministic IDs. */
function hashCode(str: string): number {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    const char = str.charCodeAt(i);
    hash = (hash << 5) - hash + char;
    hash |= 0; // Convert to 32-bit integer
  }
  return Math.abs(hash);
}

/**
 * Rich mock responses used when the backend is unreachable.
 *
 * @param prompt - The user's question
 * @returns Markdown-formatted answer
 */
function getMockResponse(prompt: string): string {
  const lower = prompt.toLowerCase();

  // ── Architecture / Overview ─────────────────────────────────────
  if (
    lower.includes('architecture') ||
    lower.includes('overview') ||
    lower.includes('what is') ||
    lower.includes('how does it work')
  ) {
    return [
      '## Microsoft FHIR Server Architecture',
      '',
      'The **Microsoft FHIR Server for Azure** is an open-source implementation of the FHIR (Fast Healthcare Interoperability Resources) standard built on top of .NET Core.',
      '',
      '### Key Layers',
      '',
      '1. **API Layer** — ASP.NET Core controllers handle RESTful FHIR requests (`Patient`, `Observation`, `Bundle`, etc.).',
      '2. **Core Layer** — Business logic: validation, search, CRUD, and transaction orchestration.',
      '3. **Data Layer** — Pluggable persistence via `IFhirDataStore`:',
      '   - **Cosmos DB** — NoSQL, globally distributed, preferred for high-scale.',
      '   - **SQL Server** — Relational, supports T-SQL queries and full-text search.',
      '4. **Search Layer** — Converts FHIR search parameters into database-specific queries.',
      '',
      '### Notable Patterns',
      '',
      '- **Mediator** — Uses MediatR for dispatching commands and queries.',
      '- **Repository** — Abstracts data access behind `IFhirRepository`.',
      '- **Pipeline** — ASP.NET Core middleware for auth, logging, and FHIR formatting.',
      '- **Options Pattern** — Configuration via `IOptions<T>` for data-store settings.',
    ].join('\n');
  }

  // ── Custom Search ────────────────────────────────────────────────
  if (
    lower.includes('search') ||
    lower.includes('query') ||
    lower.includes('find')
  ) {
    return [
      '## Custom Search in the FHIR Server',
      '',
      'FHIR search is implemented in three stages: **parsing**, **compilation**, and **execution**.',
      '',
      '### 1. Search Parameter Parsing',
      'The `SearchOptionsFactory` parses the query string into `SearchOptions`:',
      '```csharp',
      'var searchOptions = new SearchOptionsFactory().Create("name=smith&birthdate=gt2000-01-01");',
      '```',
      '',
      '### 2. Expression Compilation',
      '`SearchParameterExpressionParser` converts each FHIR search parameter into an `Expression`:',
      '```csharp',
      'Expression searchExpression = _expressionParser.Parse(searchParameter);',
      '```',
      '',
      '### 3. Data-Store Execution',
      '- **Cosmos DB** — Expressions are translated to `CosmosQueryBuilder` SQL queries.',
      '- **SQL Server** — Expressions are translated to `SqlServerSearchService` T-SQL via `ExpressionVisitors`.',
      '',
      '### Key Files',
      '```',
      'src/Microsoft.Health.Fhir.Core/Features/Search/',
      '  SearchOptionsFactory.cs',
      '  SearchService.cs',
      '  Expressions/',
      '    SearchParameterExpressionParser.cs',
      '    IExpressionVisitor.cs',
      '```',
    ].join('\n');
  }

  // ── Data Access Layer ───────────────────────────────────────────
  if (
    lower.includes('data access') ||
    lower.includes('data store') ||
    lower.includes('repository') ||
    lower.includes('database') ||
    lower.includes('cosmos') ||
    lower.includes('sql')
  ) {
    return [
      '## Data Access Layer',
      '',
      'The DAL is abstracted through `IFhirDataStore`, allowing seamless swapping between Cosmos DB and SQL Server.',
      '',
      '### Interface',
      '```csharp',
      'public interface IFhirDataStore',
      '{',
      '    Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, ...);',
      '    Task<ResourceWrapper> GetAsync(ResourceKey key, ...);',
      '    Task<SearchResult> SearchAsync(SearchOptions options, ...);',
      '    Task<DeleteOutcome> DeleteAsync(ResourceKey key, ...);',
      '}',
      '```',
      '',
      '### Cosmos DB Implementation',
      '`CosmosFhirDataStore` uses the Cosmos DB v3 SDK with `Container.UpsertItemAsync`:',
      '```csharp',
      'var response = await _container.UpsertItemAsync(resource, partitionKey);',
      '```',
      '',
      '### SQL Server Implementation',
      '`SqlServerFhirDataStore` uses Dapper + raw SQL for high-performance queries and `SqlConnection` for transactions.',
      '',
      '### Key Files',
      '```',
      'src/Microsoft.Health.Fhir.CosmosDb/Features/Storage/CosmosFhirDataStore.cs',
      'src/Microsoft.Health.Fhir.SqlServer/Features/Storage/SqlServerFhirDataStore.cs',
      'src/Microsoft.Health.Fhir.Core/Features/Persistence/IFhirDataStore.cs',
      '```',
    ].join('\n');
  }

  // ── PR / Contribution ────────────────────────────────────────────
  if (
    lower.includes('pr') ||
    lower.includes('pull request') ||
    lower.includes('contribute') ||
    lower.includes('contribution')
  ) {
    return [
      '## Creating a PR for the FHIR Server',
      '',
      '### 1. Fork & Branch',
      '```bash',
      'git clone https://github.com/microsoft/fhir-server.git',
      'git checkout -b feature/my-awesome-feature',
      '```',
      '',
      '### 2. Build & Test',
      '```bash',
      'dotnet build --configuration Release',
      'dotnet test --filter "FullyQualifiedName~UnitTests"',
      '```',
      '',
      '### 3. Code Standards',
      '- Follow the existing C# coding style (`.editorconfig` enforced).',
      '- Add unit tests for new business logic.',
      '- Add integration tests for new API surfaces.',
      '- Update `openapi.json` if you change REST contracts.',
      '',
      '### 4. PR Template',
      '- Describe the **problem** and **solution**.',
      '- Link related issues (`Fixes #123`).',
      '- Include test results or screenshots.',
      '- Tag `@microsoft/fhir-server-maintainers` for review.',
      '',
      '### 5. CI Gates',
      'All PRs must pass: **Build** | **Unit Tests** | **Integration Tests** | **Static Analysis (SonarQube)**',
    ].join('\n');
  }

  // ── Fallback ─────────────────────────────────────────────────────
  return [
    `## FHIR Server Expert Response`,
    '',
    `You asked: *"${prompt}"*`,
    '',
    'I am the **FHIR Server Expert**, specialized in Microsoft\'s open-source FHIR Server implementation. I can help you with:',
    '',
    '- **Architecture** — Understanding the layered design (API, Core, Data, Search)',
    '- **Custom Search** — Parsing, expression compilation, and data-store execution',
    '- **Data Access Layer** — `IFhirDataStore`, Cosmos DB vs SQL Server implementations',
    '- **Contributing** — PR guidelines, build steps, and CI gates',
    '',
    '> _The backend service is currently unavailable, so I\'m providing best-effort guidance based on my built-in knowledge._',
  ].join('\n');
}
