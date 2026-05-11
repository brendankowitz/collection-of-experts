/**
 * Healthcare Shared Components Expert chat participant.
 *
 * Handles chat requests for the `@healthcare-components-expert` agent.
 * Delegates to the backend API when available, or provides rich
 * mock responses when the backend is unreachable.
 */

import * as vscode from 'vscode';
import { AgentClient } from './agentClient';
import type { ChatRequest } from './types';

/** The agent identifier used when calling the backend. */
const AGENT_ID = 'healthcare-components-expert';

/** Fixed set of follow-up questions shown after each response. */
const FOLLOW_UP_QUESTIONS = [
  'What retry patterns are used?',
  'Explain the blob storage abstraction',
  'How to use the Mediator pattern?',
];

/**
 * The set of follow-up questions shown after each Healthcare Components Expert response.
 */
export const healthcareFollowUpQuestions: string[] = FOLLOW_UP_QUESTIONS;

/**
 * Create the VS Code ChatRequestHandler for the Healthcare Components Expert.
 *
 * @param client - The HTTP client for talking to the backend
 * @returns A handler compatible with vscode.chat.createChatParticipant
 */
export function createHealthcareComponentsParticipantHandler(
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
      stream.markdown('Hello! I am the **Healthcare Shared Components Expert**. Ask me about the Microsoft Healthcare Shared Components library — retry policies, blob storage, configuration management, and more.');
      return { metadata: { agentId: AGENT_ID, followups: FOLLOW_UP_QUESTIONS } };
    }

    // Show progress indicator
    stream.progress('Consulting the Healthcare Components Expert...');

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
 */
function deriveSessionId(context: vscode.ChatContext): string {
  const history = context.history;
  if (history.length === 0) {
    return `hc-${Date.now()}`;
  }
  const firstPrompt =
    history[0]?.participant === undefined
      ? String((history[0] as { prompt?: string })?.prompt ?? '')
      : '';
  return `hc-${hashCode(firstPrompt || 'default')}`;
}

/** Simple string hash for deterministic IDs. */
function hashCode(str: string): number {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    const char = str.charCodeAt(i);
    hash = (hash << 5) - hash + char;
    hash |= 0;
  }
  return Math.abs(hash);
}

/**
 * Rich mock responses used when the backend is unreachable.
 */
function getMockResponse(prompt: string): string {
  const lower = prompt.toLowerCase();

  // ── Retry Patterns ───────────────────────────────────────────────
  if (
    lower.includes('retry') ||
    lower.includes('retries') ||
    lower.includes('backoff') ||
    lower.includes('circuit breaker') ||
    lower.includes('resilience')
  ) {
    return [
      '## Retry Patterns in Healthcare Shared Components',
      '',
      'The library provides several Polly-based resilience policies under `Microsoft.Health.Core/Features/Retry`:',
      '',
      '### 1. Retry Policy',
      '```csharp',
      'var retryPolicy = Policy',
      '    .Handle<HttpRequestException>()',
      '    .WaitAndRetryAsync(3,',
      '        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),',
      '        onRetry: (exception, timeSpan, retryCount, ctx) =>',
      '        {',
      '            _logger.LogWarning(exception, "Retry {Count} after {Delay}ms", retryCount, timeSpan);',
      '        });',
      '```',
      '',
      '### 2. Circuit Breaker',
      '```csharp',
      'var circuitBreaker = Policy',
      '    .Handle<HttpRequestException>()',
      '    .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30),',
      '        onBreak: (ex, breakDelay) => _logger.LogError(ex, "Circuit broken"),',
      '        onReset: () => _logger.LogInformation("Circuit reset"));',
      '```',
      '',
      '### 3. Retryable Factories',
      '- `IRetryPolicyFactory` — creates typed retry policies for different subsystems.',
      '- `SqlRetryPolicyFactory` — SQL-specific retry with deadlock detection.',
      '- `BlobRetryPolicyFactory` — Azure Blob storage retry with throttling awareness.',
      '',
      '### Key Files',
      '```',
      'src/Microsoft.Health.Core/Features/Retry/',
      '  IRetryPolicyFactory.cs',
      '  SqlRetryPolicyFactory.cs',
      '  BlobRetryPolicyFactory.cs',
      '  RetryPolicyConfiguration.cs',
      '```',
    ].join('\n');
  }

  // ── Blob Storage Abstraction ────────────────────────────────────
  if (
    lower.includes('blob') ||
    lower.includes('storage') ||
    lower.includes('file') ||
    lower.includes('upload') ||
    lower.includes('download')
  ) {
    return [
      '## Blob Storage Abstraction',
      '',
      'The `IBlobDataStore` interface abstracts Azure Blob Storage operations, enabling easy mocking and multi-cloud portability.',
      '',
      '### Interface',
      '```csharp',
      'public interface IBlobDataStore',
      '{',
      '    Task<Stream> OpenReadAsync(string blobName, CancellationToken ct = default);',
      '    Task<long> UploadAsync(string blobName, Stream data, CancellationToken ct = default);',
      '    Task DeleteAsync(string blobName, CancellationToken ct = default);',
      '    Task<bool> ExistsAsync(string blobName, CancellationToken ct = default);',
      '    Task<Uri> GenerateSasUriAsync(string blobName, TimeSpan expiry);',
      '}',
      '```',
      '',
      '### Azure Blob Implementation',
      '`AzureBlobDataStore` wraps `Azure.Storage.Blobs.BlobContainerClient`:',
      '```csharp',
      'public class AzureBlobDataStore : IBlobDataStore',
      '{',
      '    private readonly BlobContainerClient _container;',
      '    public AzureBlobDataStore(BlobContainerClient container) => _container = container;',
      '',
      '    public async Task<long> UploadAsync(string blobName, Stream data, ...)',
      '    {',
      '        var blob = _container.GetBlobClient(blobName);',
      '        var response = await blob.UploadAsync(data, overwrite: true);',
      '        return response.Value.ContentLength;',
      '    }',
      '}',
      '```',
      '',
      '### Streaming Uploads',
      'Large FHIR bundles are uploaded using `BlockBlobClient` with parallel block uploads for performance:',
      '```csharp',
      'await blobClient.UploadAsync(data, new BlobUploadOptions',
      '{',
      '    TransferOptions = new StorageTransferOptions',
      '    {',
      '        MaximumConcurrency = 4,',
      '        MaximumTransferSize = 4 * 1024 * 1024, // 4 MB blocks',
      '    }',
      '});',
      '```',
      '',
      '### Key Files',
      '```',
      'src/Microsoft.Health.Blob/Features/Storage/',
      '  IBlobDataStore.cs',
      '  AzureBlobDataStore.cs',
      '  BlobClientProvider.cs',
      '  BlobDataStoreConfiguration.cs',
      '```',
    ].join('\n');
  }

  // ── Mediator Pattern ─────────────────────────────────────────────
  if (
    lower.includes('mediator') ||
    lower.includes('mediatr') ||
    lower.includes('cqrs') ||
    lower.includes('command') ||
    lower.includes('handler')
  ) {
    return [
      '## Mediator Pattern',
      '',
      'Healthcare Shared Components uses **MediatR** to decouple command/query dispatching from their handlers.',
      '',
      '### IRequest and IRequestHandler',
      '```csharp',
      '// Query',
      'public class GetPatientQuery : IRequest<PatientDto>',
      '{',
      '    public string PatientId { get; set; }',
      '}',
      '',
      '// Handler',
      'public class GetPatientHandler : IRequestHandler<GetPatientQuery, PatientDto>',
      '{',
      '    private readonly IPatientRepository _repo;',
      '    public GetPatientHandler(IPatientRepository repo) => _repo = repo;',
      '',
      '    public async Task<PatientDto> Handle(GetPatientQuery request, CancellationToken ct)',
      '    {',
      '        var patient = await _repo.GetAsync(request.PatientId, ct);',
      '        return PatientDto.FromEntity(patient);',
      '    }',
      '}',
      '```',
      '',
      '### Pipeline Behaviors',
      'Cross-cutting concerns are implemented as `IPipelineBehavior`:',
      '```csharp',
      'public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>',
      '{',
      '    public async Task<TResponse> Handle(TRequest request, ... , CancellationToken ct)',
      '    {',
      '        _logger.LogInformation("Handling {Request}", typeof(TRequest).Name);',
      '        var response = await next();',
      '        _logger.LogInformation("Handled {Request}", typeof(TRequest).Name);',
      '        return response;',
      '    }',
      '}',
      '```',
      '',
      '### Registration (DI)',
      '```csharp',
      'services.AddMediatR(cfg => {',
      '    cfg.RegisterServicesFromAssemblyContaining<Startup>();',
      '    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));',
      '    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));',
      '});',
      '```',
      '',
      '### Key Files',
      '```',
      'src/Microsoft.Health.Core/Features/Mediator/',
      '  LoggingBehavior.cs',
      '  RetryBehavior.cs',
      '  ValidationBehavior.cs',
      '```',
    ].join('\n');
  }

  // ── Configuration Management ────────────────────────────────────
  if (
    lower.includes('config') ||
    lower.includes('settings') ||
    lower.includes('options') ||
    lower.includes('appsettings')
  ) {
    return [
      '## Configuration Management',
      '',
      'The library uses the .NET **Options Pattern** with strong-typed configuration classes.',
      '',
      '### Example Configuration Class',
      '```csharp',
      'public class BlobStorageConfiguration : IConfigurationLocation',
      '{',
      '    public string ConnectionString { get; set; }',
      '    public string ContainerName { get; set; } = "fhir-data";',
      '    public int MaxRetries { get; set; } = 3;',
      '',
      '    public string SectionName => "BlobStorage";',
      '}',
      '```',
      '',
      '### Validation',
      'Configurations implement `IValidatableObject` or use **FluentValidation**:',
      '```csharp',
      'public class BlobStorageConfigurationValidator : AbstractValidator<BlobStorageConfiguration>',
      '{',
      '    public BlobStorageConfigurationValidator()',
      '    {',
      '        RuleFor(x => x.ConnectionString).NotEmpty();',
      '        RuleFor(x => x.ContainerName).NotEmpty().MaximumLength(63);',
      '    }',
      '}',
      '```',
      '',
      '### Key Files',
      '```',
      'src/Microsoft.Health.Core/Features/Configuration/',
      '  IConfigurationLocation.cs',
      '  ConfigurationValidator.cs',
      '  ConfigurationModule.cs',
      '```',
    ].join('\n');
  }

  // ── Fallback ─────────────────────────────────────────────────────
  return [
    `## Healthcare Components Expert Response`,
    '',
    `You asked: *"${prompt}"*`,
    '',
    'I am the **Healthcare Shared Components Expert**, specialized in Microsoft\'s Healthcare Shared Components library. I can help you with:',
    '',
    '- **Retry Patterns** — Polly-based retry, circuit breakers, and backoff strategies',
    '- **Blob Storage** — `IBlobDataStore`, streaming uploads, and SAS token generation',
    '- **Mediator Pattern** — MediatR commands, queries, and pipeline behaviors',
    '- **Configuration** — Strong-typed options, validation, and DI registration',
    '',
    '> _The backend service is currently unavailable, so I\'m providing best-effort guidance based on my built-in knowledge._',
  ].join('\n');
}
