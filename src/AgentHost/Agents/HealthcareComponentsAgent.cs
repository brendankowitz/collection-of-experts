using System.Runtime.CompilerServices;
using System.Text;
using AgentHost.A2A;
using AgentHost.Services;

namespace AgentHost.Agents;

/// <summary>
/// Expert agent specialised in the <c>microsoft/healthcare-shared-components</c> repository.
/// Provides guidance on SQL connection wrappers, blob storage utilities, exception handling,
/// configuration management, Mediator patterns, and health checks.
/// </summary>
public sealed class HealthcareComponentsAgent : IExpertAgent
{
    private readonly MockCodeIndexService _codeIndex;
    private readonly ILogger<HealthcareComponentsAgent> _logger;
    private readonly IServiceProvider _services;

    /// <inheritdoc />
    public string AgentId => "healthcare-components-expert";

    /// <inheritdoc />
    public string Name => "Healthcare Shared Components Expert";

    /// <summary>
    /// Creates a new <see cref="HealthcareComponentsAgent"/>.
    /// </summary>
    public HealthcareComponentsAgent(MockCodeIndexService codeIndex, IServiceProvider services, ILogger<HealthcareComponentsAgent> logger)
    {
        _codeIndex = codeIndex;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public AgentCard GetAgentCard() => new()
    {
        AgentId = AgentId,
        Name = Name,
        Description = "Expert agent specialised in Microsoft Healthcare Shared Components. Covers SQL connection management, blob storage, exception handling, configuration, Mediator, and health checks.",
        Version = "1.0.0",
        Url = "http://localhost:5002",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "code-search",
                Name = "Shared Components Code Search",
                Description = "Search the microsoft/healthcare-shared-components repository.",
                ExampleQueries = ["Find RetrySqlConnectionWrapper", "Show me the blob client"]
            },
            new AgentSkill
            {
                Id = "architecture-qa",
                Name = "Shared Components Architecture Q&A",
                Description = "Explain how shared components fit together and are consumed by downstream services.",
                ExampleQueries = ["How does the retry wrapper work?", "Explain the Mediator pattern usage"]
            },
            new AgentSkill
            {
                Id = "pr-guidance",
                Name = "PR Guidance",
                Description = "Step-by-step guidance for creating pull requests.",
                ExampleQueries = ["How do I version a new shared package?"]
            }
        ]
    };

    /// <inheritdoc />
    public async Task<string> ProcessMessageAsync(string message, string sessionId)
    {
        _logger.LogInformation("[{AgentId}] Processing message in session {SessionId}: {Message}",
            AgentId, sessionId, message);

        var lowered = message.ToLowerInvariant();

        // Cross-boundary detection: FHIR server questions should be handed off
        if (IsFhirCrossBoundary(lowered))
        {
            var fhirAgent = _services.GetService(typeof(FhirServerAgent)) as FhirServerAgent;
            if (fhirAgent is not null)
            {
                _logger.LogInformation("[{AgentId}] Handing off cross-boundary question to {TargetAgent}", AgentId, fhirAgent.AgentId);
                var handoffNotice = $"\ud83d\udd04 **Handing off to {fhirAgent.Name}**...\n\n";
                var fhirResponse = await fhirAgent.ProcessMessageAsync(message, sessionId);
                return handoffNotice + fhirResponse;
            }
        }

        // SQL / Connection questions
        if (lowered.Contains("sql") || lowered.Contains("connection") || lowered.Contains("retry") || lowered.Contains("database") || lowered.Contains("transaction"))
        {
            return BuildSqlResponse(lowered);
        }

        // Blob storage questions
        if (lowered.Contains("blob") || lowered.Contains("storage") || lowered.Contains("upload") || lowered.Contains("download"))
        {
            return BuildBlobResponse(lowered);
        }

        // Exception handling
        if (lowered.Contains("exception") || lowered.Contains("error") || lowered.Contains("fault") || lowered.Contains("handling"))
        {
            return BuildExceptionHandlingResponse(lowered);
        }

        // Mediator / pattern questions
        if (lowered.Contains("mediat") || lowered.Contains("pattern") || lowered.Contains("handler") || lowered.Contains("request"))
        {
            return BuildMediatorResponse(lowered);
        }

        // Change feed questions
        if (lowered.Contains("change feed") || lowered.Contains("ichangefeed") || lowered.Contains("feed"))
        {
            return BuildChangeFeedResponse();
        }

        // Health checks
        if (lowered.Contains("health check") || lowered.Contains("healthcheck") || lowered.Contains("liveness") || lowered.Contains("readiness"))
        {
            return BuildHealthCheckResponse(lowered);
        }

        // Configuration
        if (lowered.Contains("config") || lowered.Contains("settings") || lowered.Contains("options"))
        {
            return BuildConfigurationResponse(lowered);
        }

        // Code search
        if (lowered.Contains("code") || lowered.Contains("file") || lowered.Contains("class") || lowered.Contains("implementation") || lowered.Contains("where is"))
        {
            var results = _codeIndex.Search("healthcare-shared-components", message);
            if (results.Count > 0)
                return BuildCodeSearchResponse(results);
            return $"I searched the `microsoft/healthcare-shared-components` repository but could not find any files matching \"{message}\". Could you refine your query?";
        }

        // PR guidance
        if (lowered.Contains("pr") || lowered.Contains("pull request") || lowered.Contains("branch") || lowered.Contains("merge"))
        {
            return BuildPrGuidanceResponse(lowered);
        }

        return BuildFallbackResponse(message);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(
        string message,
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var fullText = await ProcessMessageAsync(message, sessionId);
        var words = fullText.Split(' ');

        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (ct.IsCancellationRequested)
                yield break;

            sb.Append(word).Append(' ');
            if (sb.ToString().Split(' ').Length % 5 == 0)
            {
                yield return sb.ToString();
                sb.Clear();
                await Task.Delay(30, ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString().TrimEnd();
    }

    // ─── Cross-boundary Detection ────────────────────────────────────────────

    private static bool IsFhirCrossBoundary(string lowered)
    {
        var fhirKeywords = new[]
        {
            "fhir", "patient", "observation", "encounter", "resource",
            "bundle", "search parameter", "$export", "smart on fhir",
            "capability statement", "operation outcome", "r4", "r5",
            "hl7", "compartment", "valueset", "codesystem", "subscription"
        };

        return fhirKeywords.Any(k => lowered.Contains(k));
    }

    // ─── SQL Response Builder ────────────────────────────────────────────────

    private static string BuildSqlResponse(string lowered)
    {
        if (lowered.Contains("retry"))
        {
            return """
                ## RetrySqlConnectionWrapper

                The `RetrySqlConnectionWrapper` is a resilient decorator around `System.Data.SqlClient.SqlConnection` that transparently retries transient failures.

                ### Key Features
                - **Transient fault detection** – Uses `SqlException` error numbers (e.g., -2, 20, 64, 233) to identify retryable failures
                - **Exponential backoff** – Configurable retry policy with jitter to avoid thundering herds
                - **Connection-state awareness** – Only retries when the connection is in a safe state to retry
                - **Logging** – Emits structured logs for every retry attempt with timing

                ### Usage
                ```csharp
                // Registered in DI as a decorator
                services.AddSqlServerConnectionWrapper(
                    configuration.GetConnectionString("SqlServer"),
                    retryOptions =>
                    {
                        retryOptions.MaxRetries = 5;
                        retryOptions.InitialDelay = TimeSpan.FromMilliseconds(100);
                        retryOptions.MaxDelay = TimeSpan.FromSeconds(30);
                    });
                ```

                ### Source File
                `src/Microsoft.Health.SqlServer/Features/Storage/RetrySqlConnectionWrapper.cs`

                ### How It Works
                1. Intercepts `OpenAsync()` and `Execute*Async()` calls
                2. Catches `SqlException` and `TimeoutException`
                3. Checks if the exception is in the transient error list
                4. Waits (backoff) and retries up to `MaxRetries`
                5. On final failure, throws the original exception with retry metadata

                ### Configuration Options
                | Option | Default | Description |
                |--------|---------|-------------|
                | `MaxRetries` | 5 | Maximum retry attempts |
                | `InitialDelay` | 100ms | First retry delay |
                | `MaxDelay` | 30s | Cap on delay between retries |
                | `BackoffMultiplier` | 2.0 | Exponential growth factor |
                """;
        }

        if (lowered.Contains("transaction"))
        {
            return """
                ## SqlTransactionScope

                The shared components provide a managed transaction scope that wraps `TransactionScope` with healthcare-specific defaults.

                ### Key Features
                - **Automatic enlistment** – Detects ambient transactions and enlists the connection
                - **Timeout handling** – Configurable transaction timeout with sensible defaults
                - **Isolation level control** – Defaults to `ReadCommitted`, overridable per operation
                - **Nested transaction support** – Handles nested scopes via `TransactionScopeOption.Required`

                ### Usage
                ```csharp
                using var transaction = _sqlTransactionScope.BeginTransaction();
                // ... perform database operations ...
                transaction.Complete();
                ```

                ### Source File
                `src/Microsoft.Health.SqlServer/Features/Storage/SqlTransactionScope.cs`
                """;
        }

        return """
            ## SQL Connection Management in Healthcare Shared Components

            The `Microsoft.Health.SqlServer` package provides resilient database connectivity used by both the FHIR server and DICOM server.

            ### Components
            1. **RetrySqlConnectionWrapper** – Transparent retry for transient SQL failures with exponential backoff
            2. **SqlServerDataStoreConfiguration** – Strongly-typed configuration for connection strings, timeouts, and retry policy
            3. **SqlTransactionScope** – Managed `TransactionScope` with healthcare defaults
            4. **SqlServerSchemaManager** – Schema version detection and migration orchestration
            5. **SqlServerRegistrationExtension** – One-line DI registration: `services.AddSqlServerConnection(...)`

            ### Key Facts
            - **RetrySqlConnectionWrapper** handles 20+ known transient error codes automatically
            - **Connection pooling** is preserved; retries happen at the logical operation level
            - **Schema versioning** uses a `SchemaVersion` table to track applied migrations
            - **Health checks** include a SQL connectivity check that uses the wrapper
            - Both **Managed Identity** and **SQL authentication** are supported
            """;
    }

    // ─── Blob Storage Response Builder ───────────────────────────────────────

    private static string BuildBlobResponse(string lowered)
    {
        return """
            ## Blob Storage Utilities

            The `Microsoft.Health.Blob` package wraps Azure Blob Storage with retry policies and health-care-specific helpers.

            ### IBlobClient (and BlobClient)
            - Wraps `Azure.Storage.Blobs.BlobClient` with a simplified interface
            - Adds retry on transient Azure errors (HTTP 500, 503, 408, etc.)
            - Supports streaming uploads and downloads without buffering the entire blob in memory

            ### Key APIs
            ```csharp
            public interface IBlobClient
            {
                Task<Stream> OpenReadAsync(string blobName, CancellationToken ct = default);
                Task UploadAsync(string blobName, Stream content, CancellationToken ct = default);
                Task<bool> ExistsAsync(string blobName, CancellationToken ct = default);
                Task DeleteAsync(string blobName, CancellationToken ct = default);
            }
            ```

            ### Configuration
            ```json
            {
              "BlobStore": {
                "ConnectionString": "DefaultEndpointsProtocol=https;...",
                "ContainerName": "fhirdata"
              }
            }
            ```

            ### Source File
            `src/Microsoft.Health.Blob/Features/Storage/BlobClient.cs`

            ### Key Facts
            - **Retry policy**: Exponential backoff with up to 5 retries for transient Azure errors
            - **Streaming**: `OpenReadAsync` returns a forward-only stream; ideal for large export files
            - **Health check**: `BlobHealthCheck` verifies container accessibility on startup
            - **Managed Identity**: Supports `BlobServiceClient` construction via `DefaultAzureCredential`
            """;
    }

    // ─── Exception Handling Response Builder ─────────────────────────────────

    private static string BuildExceptionHandlingResponse(string lowered)
    {
        return """
            ## Exception Handling Utilities

            The shared components include structured exception types and middleware for consistent error handling across healthcare services.

            ### Custom Exception Types
            | Exception | HTTP Status | Usage |
            |-----------|-------------|-------|
            | `BadRequestException` | 400 | Invalid client input |
            | `UnauthorizedException` | 401 | Authentication failure |
            | `ForbiddenException` | 403 | Authorisation failure |
            | `ResourceNotFoundException` | 404 | Missing resource |
            | `MethodNotAllowedException` | 405 | Unsupported HTTP method |
            | `ConflictException` | 409 | Concurrent modification |
            | `TooManyRequestsException` | 429 | Rate-limit exceeded |
            | `ServiceUnavailableException` | 503 | Temporary server failure |

            ### Exception Handling Middleware
            `ExceptionHandlingMiddleware` catches all unhandled exceptions and serialises them as JSON Problem Details (RFC 7807).

            ```csharp
            app.UseExceptionHandlingMiddleware();
            ```

            ### Source Files
            - `src/Microsoft.Health.Core/Exceptions/*.cs`
            - `src/Microsoft.Health.Api/Features/ExceptionHandling/ExceptionHandlingMiddleware.cs`

            ### Key Facts
            - All exceptions carry a structured log event ID for correlation
            - PII (Personally Identifiable Information) is automatically scrubbed from error responses
            - Inner exceptions are logged but not exposed to the client
            - The middleware integrates with `IAuditLogger` for security-relevant failures
            """;
    }

    // ─── Mediator Response Builder ───────────────────────────────────────────

    private static string BuildMediatorResponse(string lowered)
    {
        return """
            ## Mediator Pattern Usage

            Healthcare services use the **MediatR** library to implement the Mediator pattern, decoupling request senders from handlers.

            ### Core Abstractions
            ```csharp
            public interface IRequestHandler<in TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
            }
            ```

            ### Example: Create Resource Request
            ```csharp
            public class CreateResourceRequest : IRequest<CreateResourceResponse>
            {
                public Resource Resource { get; init; }
                public string ResourceType { get; init; }
            }

            public class CreateResourceHandler : IRequestHandler<CreateResourceRequest, CreateResourceResponse>
            {
                private readonly IFhirDataStore _dataStore;

                public CreateResourceHandler(IFhirDataStore dataStore)
                {
                    _dataStore = dataStore;
                }

                public async Task<CreateResourceResponse> Handle(CreateResourceRequest request, CancellationToken ct)
                {
                    var result = await _dataStore.UpsertAsync(request.Resource, ct);
                    return new CreateResourceResponse(result);
                }
            }
            ```

            ### Pipeline Behaviours
            MediatR pipeline behaviours add cross-cutting concerns:
            - **ValidationBehaviour** – Validates requests with FluentValidation before the handler runs
            - **LoggingBehaviour** – Logs request duration and outcomes
            - **AuditBehaviour** – Writes audit events for sensitive operations
            - **RetryBehaviour** – Retries transient failures at the request level

            ### Source Files
            - Handlers are typically in `Features/<Feature>/`
            - Pipeline behaviours are in `Features/Pipeline/`

            ### Key Facts
            - Every request has exactly one handler; one-to-many is handled via notifications
            - Handlers are registered automatically via assembly scanning
            - Pipeline behaviours execute in registration order (outer -> inner)
            - Cancellation tokens flow from the controller through MediatR to the data store
            """;
    }

    // ─── Change Feed Response Builder ────────────────────────────────────────

    private static string BuildChangeFeedResponse()
    {
        return """
            ## IChangeFeedSource

            `IChangeFeedSource` is an abstraction for consuming ordered change streams from underlying data stores. It is used by the FHIR server to drive subscriptions and notifications.

            ### Interface
            ```csharp
            public interface IChangeFeedSource
            {
                IAsyncEnumerable<ChangeFeedEntry> ReadChangesAsync(
                    ChangeFeedOptions options,
                    CancellationToken ct = default);
            }
            ```

            ### Cosmos DB Implementation (`CosmosChangeFeedSource`)
            - Wraps the Cosmos DB Change Feed processor
            - Provides at-least-once delivery guarantees
            - Supports checkpointing via a persistent lease container
            - Handles partition splits and failures automatically

            ### SQL Implementation (`SqlChangeFeedSource`)
            - Polls a `ResourceChangeData` table for new rows
            - Uses a high-water mark (last-sequence-number) for checkpointing
            - Supports configurable poll intervals

            ### ChangeFeedEntry
            ```csharp
            public class ChangeFeedEntry
            {
                public string ResourceId { get; init; }
                public string ResourceType { get; init; }
                public int Version { get; init; }
                public DateTimeOffset Timestamp { get; init; }
                public ChangeType ChangeType { get; init; } // Create, Update, Delete
            }
            ```

            ### Source File
            `src/Microsoft.Health.Core/Features/ChangeFeed/IChangeFeedSource.cs`

            ### Key Facts
            - **At-least-once delivery**: Consumers must be idempotent
            - **Ordering**: Guarantees ordering within a partition; global ordering depends on the implementation
            - **Subscriptions**: The FHIR `$subscription` hook uses this to push resource changes to external endpoints
            """;
    }

    // ─── Health Check Response Builder ───────────────────────────────────────

    private static string BuildHealthCheckResponse(string lowered)
    {
        return """
            ## Health Checks

            The shared components provide a suite of health checks for ASP.NET Core's `HealthChecks` middleware.

            ### Built-in Health Checks
            | Check | Class | Description |
            |-------|-------|-------------|
            | SQL Connectivity | `SqlServerHealthCheck` | Opens a connection to the configured SQL Server |
            | Blob Storage | `BlobHealthCheck` | Verifies blob container accessibility |
            | Cosmos DB | `CosmosHealthCheck` | Reads the Cosmos DB account metadata |
            | Schema Version | `SchemaVersionHealthCheck` | Confirms the DB schema is at the expected version |
            | Service Bus | `ServiceBusHealthCheck` | Pings the Azure Service Bus namespace |

            ### Registration (one-liner)
            ```csharp
            services.AddHealthChecks()
                .AddSqlServer(configuration)
                .AddBlobStorage(configuration)
                .AddCosmosDb(configuration)
                .AddCheck<SchemaVersionHealthCheck>("schema-version");
            ```

            ### Endpoints
            ```csharp
            app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("liveness")
            });
            app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("readiness")
            });
            ```

            ### Source Files
            `src/Microsoft.Health.*.*/Features/Health/`

            ### Key Facts
            - **Liveness** probes verify the process is running (`/health/live`)
            - **Readiness** probes verify all dependencies are reachable (`/health/ready`)
            - Health check results are cached for a configurable duration to prevent DB overload
            - Kubernetes-ready: returns HTTP 200 when healthy, 503 when unhealthy
            """;
    }

    // ─── Configuration Response Builder ──────────────────────────────────────

    private static string BuildConfigurationResponse(string lowered)
    {
        return """
            ## Configuration Management

            The shared components use `IOptions<T>` and `IOptionsMonitor<T>` patterns for strongly-typed configuration.

            ### Key Configuration Types
            ```csharp
            public class SqlServerDataStoreConfiguration
            {
                public string ConnectionString { get; set; }
                public int CommandTimeoutSeconds { get; set; } = 30;
                public RetryConfiguration Retry { get; set; } = new();
                public bool AllowDatabaseCreation { get; set; } = false;
            }

            public class RetryConfiguration
            {
                public int MaxRetries { get; set; } = 5;
                public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
                public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
            }
            ```

            ### Validation
            Configuration is validated at startup using `IValidateOptions<T>`:
            ```csharp
            public class SqlServerConfigurationValidator : IValidateOptions<SqlServerDataStoreConfiguration>
            {
                public ValidateOptionsResult Validate(string name, SqlServerDataStoreConfiguration options)
                {
                    if (string.IsNullOrWhiteSpace(options.ConnectionString))
                        return ValidateOptionsResult.Fail("ConnectionString is required.");
                    return ValidateOptionsResult.Success;
                }
            }
            ```

            ### Source Files
            - `src/Microsoft.Health.SqlServer/Configs/`
            - `src/Microsoft.Health.Blob/Configs/`
            - `src/Microsoft.Health.Core/Configs/`

            ### Key Facts
            - All configuration classes have sensible defaults to reduce boilerplate
            - Validation failures throw on startup, preventing the service from running with invalid config
            - Secret connection strings should use Key Vault references, not plain text
            """;
    }

    // ─── Code Search Response Builder ────────────────────────────────────────

    private static string BuildCodeSearchResponse(List<(string FilePath, string Snippet)> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Code Search Results ({results.Count} files found)");
        sb.AppendLine();

        foreach (var (path, snippet) in results)
        {
            sb.AppendLine($"### `{path}`");
            sb.AppendLine("```csharp");
            sb.AppendLine(snippet);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("Would you like me to explain how any of these files work?");
        return sb.ToString();
    }

    // ─── PR Guidance Response Builder ────────────────────────────────────────

    private static string BuildPrGuidanceResponse(string lowered)
    {
        return """
            ## PR Guidance for microsoft/healthcare-shared-components

            Shared-components PRs affect **multiple downstream services** (FHIR, DICOM, etc.), so they require extra care.

            ### 1. Branch Naming
            ```
            feature/[package]-[description]
            ```
            Example: `feature/sqlserver-retry-policy-update`

            ### 2. Versioning
            - The repo uses **SemVer** for all NuGet packages
            - A PR that changes public API surface must bump the **minor** version
            - Bug fixes bump the **patch** version
            - Breaking changes bump the **major** version and require a migration guide
            - Update `Directory.Build.props` or the individual `.csproj` `<VersionPrefix>`

            ### 3. Testing Requirements
            - **Unit tests**: Every new public method needs a unit test
            - **Integration tests**: Database-related changes must pass against SQL LocalDB and/or Cosmos Emulator
            - **Downstream compatibility**: Run a test build of `microsoft/fhir-server` against your local package
            - **Performance tests**: For retry or connection changes, benchmark latency under load

            ### 4. PR Checklist
            - [ ] Version bumped correctly
            - [ ] Public API changes documented in `CHANGELOG.md`
            - [ ] Unit tests added with >80% coverage on new code
            - [ ] Integration tests pass locally (`dotnet test`)
            - [ ] Downstream build verified (FHIR server compiles against local package)
            - [ ] XML doc comments added for new public APIs
            - [ ] No breaking changes without approval from `@microsoft/healthcare-admins`

            ### 5. Review Process
            - All shared-components PRs require **2 approvals** from code owners
            - CI runs the full matrix: Windows + Linux, .NET 8 + .NET 9
            - Packages are published to the internal NuGet feed only after merge to `main`
            """;
    }

    // ─── Fallback Response Builder ───────────────────────────────────────────

    private static string BuildFallbackResponse(string message)
    {
        return $"""
            I'm the **Healthcare Shared Components Expert**, specialising in the `microsoft/healthcare-shared-components` repository. I can help with:

            - **SQL Connection Management** – `RetrySqlConnectionWrapper`, transactions, schema migrations
            - **Blob Storage** – `IBlobClient`, streaming uploads, retry policies
            - **Exception Handling** – Structured exceptions, middleware, PII scrubbing
            - **Mediator Pattern** – MediatR handlers, pipeline behaviours
            - **Change Feed** – `IChangeFeedSource`, Cosmos and SQL implementations
            - **Health Checks** – Liveness, readiness, custom checks
            - **Configuration** – Strongly-typed options, validation

            ### Key Facts About Shared Components
            1. **RetrySqlConnectionWrapper** – Resilient SQL connection wrapper with exponential backoff for 20+ transient error codes
            2. **BlobClient** – Streaming-aware Azure Blob wrapper with built-in retry and health check
            3. **IChangeFeedSource** – Unified change-feed abstraction over Cosmos DB and SQL polling
            4. **Mediator Pattern** – MediatR-based request pipeline with validation, logging, and audit behaviours
            5. **HealthChecks** – Kubernetes-ready liveness and readiness probes for SQL, Blob, Cosmos, and schema version
            6. **Structured Exceptions** – PII-safe exception hierarchy mapped to HTTP status codes
            7. **Configuration Validation** – Startup-time validation prevents services from running with invalid config

            Your question: "{message}"

            Could you clarify which area you're interested in? Or try one of my example queries!
            """;
    }
}
