using System.Runtime.CompilerServices;
using System.Text;
using AgentHost.A2A;
using AgentHost.Services;

namespace AgentHost.Agents;

/// <summary>
/// Expert agent specialised in the <c>microsoft/fhir-server</c> repository.
/// Provides architecture guidance, code search, and PR creation help.
/// </summary>
public sealed class FhirServerAgent : IExpertAgent
{
    private readonly MockCodeIndexService _codeIndex;
    private readonly ILogger<FhirServerAgent> _logger;

    /// <inheritdoc />
    public string AgentId => "fhir-server-expert";

    /// <inheritdoc />
    public string Name => "FHIR Server Expert";

    /// <summary>
    /// Creates a new <see cref="FhirServerAgent"/>.
    /// </summary>
    public FhirServerAgent(MockCodeIndexService codeIndex, ILogger<FhirServerAgent> logger)
    {
        _codeIndex = codeIndex;
        _logger = logger;
    }

    /// <inheritdoc />
    public AgentCard GetAgentCard() => new()
    {
        AgentId = AgentId,
        Name = Name,
        Description = "Expert agent specialised in the Microsoft FHIR Server for Azure. Answers architecture questions, searches code, and guides PRs.",
        Version = "1.0.0",
        Url = "http://localhost:5001",
        Capabilities = new AgentCapabilities { Streaming = true },
        Skills =
        [
            new AgentSkill
            {
                Id = "code-search",
                Name = "FHIR Code Search",
                Description = "Search the microsoft/fhir-server repository.",
                ExampleQueries = ["Find the search parameter registry", "Show me $export implementation"]
            },
            new AgentSkill
            {
                Id = "architecture-qa",
                Name = "FHIR Architecture Q&A",
                Description = "Explain architectural decisions, data flow, and component interactions.",
                ExampleQueries = ["How does R4 search work?", "Explain the data layer abstraction"]
            },
            new AgentSkill
            {
                Id = "pr-guidance",
                Name = "PR Guidance",
                Description = "Step-by-step guidance for creating pull requests.",
                ExampleQueries = ["How do I submit a PR for a new search parameter?"]
            }
        ]
    };

    /// <inheritdoc />
    public async Task<string> ProcessMessageAsync(string message, string sessionId)
    {
        _logger.LogInformation("[{AgentId}] Processing message in session {SessionId}: {Message}",
            AgentId, sessionId, message);

        var lowered = message.ToLowerInvariant();

        // Architecture questions
        if (lowered.Contains("architecture") || lowered.Contains("how does") || lowered.Contains("data flow") || lowered.Contains("explain"))
        {
            if (lowered.Contains("search") || lowered.Contains("parameter"))
                return BuildSearchArchitectureResponse();

            if (lowered.Contains("export") || lowered.Contains("$export"))
                return BuildExportArchitectureResponse();

            if (lowered.Contains("data layer") || lowered.Contains("database") || lowered.Contains("cosmos") || lowered.Contains("sql"))
                return BuildDataLayerArchitectureResponse();

            if (lowered.Contains("auth") || lowered.Contains("smart") || lowered.Contains("security"))
                return BuildAuthArchitectureResponse();

            return BuildGeneralArchitectureResponse();
        }

        // Code search
        if (lowered.Contains("code") || lowered.Contains("file") || lowered.Contains("class") || lowered.Contains("implementation") || lowered.Contains("where is"))
        {
            var results = _codeIndex.Search("fhir-server", message);
            if (results.Count > 0)
                return BuildCodeSearchResponse(results);
            return $"I searched the `microsoft/fhir-server` repository but could not find any files matching \"{message}\". Could you refine your query or try different keywords?";
        }

        // PR guidance
        if (lowered.Contains("pr") || lowered.Contains("pull request") || lowered.Contains("branch") || lowered.Contains("merge"))
        {
            return BuildPrGuidanceResponse(lowered);
        }

        // General / fallback
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
            // Yield every 3-7 words to simulate realistic streaming
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

    // ─── Architecture Response Builders ──────────────────────────────────────

    private static string BuildSearchArchitectureResponse()
    {
        return """
            ## FHIR Server Search Architecture

            The Microsoft FHIR Server implements the HL7 FHIR R4 search specification with several key components:

            ### 1. Search Parameter Registry (`SearchParameterRegistry.cs`)
            - Maintains an in-memory registry of all active search parameters
            - Supports both built-in FHIR search parameters and custom ones defined via the `SearchParameter` resource
            - Periodically refreshes from the underlying data store to pick up runtime changes

            ### 2. Search Options Factory (`SearchOptionsFactory.cs`)
            - Parses the incoming query string (e.g., `?name=smith&birthdate=gt2000-01-01`)
            - Validates that each parameter is supported for the requested resource type
            - Applies chaining (e.g., `Patient?organization.name=Hospital`) by expanding into sub-queries
            - Handles modifiers like `:exact`, `:contains`, `:missing`, and `:text`

            ### 3. Search Service (`SearchService.cs`)
            - Orchestrates the search pipeline
            - Calls the appropriate `ISearchService` implementation based on configuration
            - Wraps results into a `Bundle` with correct `self`, `next`, and `prev` links

            ### 4. SQL Search Provider (`SqlSearchService.cs`)
            - Translates search options into parameterized T-SQL
            - Uses a denormalised search parameter table (`StringSearchParam`, `TokenSearchParam`, `DateTimeSearchParam`, etc.)
            - Implements Include and Reverse Include via CTEs (Common Table Expressions)
            - Sorting is handled via indexed columns on the search parameter tables

            ### 5. Cosmos DB Search Provider (`CosmosSearchService.cs`)
            - Translates search options into Cosmos DB SQL queries
            - Uses a flattened document model with composite indexes for performance
            - Continuation tokens are used for paging rather than OFFSET

            ### Key Facts
            - **R4 Support**: Full R4 search parameter coverage with quarterly compliance updates
            - **Custom Search Parameters**: Admins can POST `SearchParameter` resources; the registry picks them up within minutes
            - **SQL Back-end**: Uses row-versioning for optimistic concurrency on search parameter tables
            - **Performance**: The SQL provider supports query plan hints and partition elimination for large datasets
            """;
    }

    private static string BuildExportArchitectureResponse()
    {
        return """
            ## Bulk Export ($export) Architecture

            The FHIR Server supports the HL7 FHIR Bulk Data Access IG ($export operation) via these components:

            ### 1. Export Controller (`ExportController.cs`)
            - Handles GET and POST requests to `/$export` and `[type]/$export`
            - Validates the `_type`, `_since`, and `_typeFilter` parameters
            - Returns a `202 Accepted` with a `Content-Location` polling URL

            ### 2. Export Job Factory (`ExportJobFactory.cs`)
            - Creates an `ExportJobRecord` that tracks the job in the database
            - Partitions the work by resource type to enable parallel processing

            ### 3. Export Job Worker (`ExportJobWorker.cs`)
            - A hosted service that polls for pending export jobs
            - Dispatches jobs to `ExportProcessingJob` instances

            ### 4. Export Processing Job (`ExportProcessingJob.cs`)
            - Reads resources in pages from the data layer
            - Serialises each page to NDJSON (Newline Delimited JSON)
            - Uploads NDJSON files to configured blob storage (Azure Blob or file system)
            - Updates the `ExportJobRecord` with progress and output file references

            ### 5. Export Anonymisation (`AnonymizationEngine.cs`)
            - Optional step that applies an anonymisation configuration to exported resources
            - Supports date-shifting, redaction, and generalisation rules

            ### Key Facts
            - **NDJSON Output**: Each resource type gets its own `.ndjson` file (e.g., `Patient.ndjson`)
            - **Parallelism**: Controlled via `Parallelism` setting; default is 10 concurrent resource types
            - **SMART on FHIR Auth**: Export jobs honour the access token's granted scopes; only authorised resource types are exported
            - **Large Datasets**: Tested with 100M+ resources; streaming keeps memory usage flat
            """;
    }

    private static string BuildDataLayerArchitectureResponse()
    {
        return """
            ## Data Layer Architecture

            The Microsoft FHIR Server uses a clean abstraction over the persistence layer, allowing the same code to run on SQL Server or Cosmos DB.

            ### 1. Core Abstractions
            - `IFhirDataStore` – CRUD operations for FHIR resources
            - `ISearchService` – Search operations
            - `IFhirOperationDataStore` – Operations like `$export`, `$convert`, `$member-match`
            - `IBulkDeleteService` – Bulk deletions

            ### 2. SQL Server Provider (`Microsoft.Health.Fhir.SqlServer`)
            - Uses a schema-based approach (Schema versions 1 through the current version)
            - Key tables:
              - `Resource` – stores the raw JSON, version, last-updated timestamp
              - `StringSearchParam`, `TokenSearchParam`, `NumberSearchParam`, `DateTimeSearchParam`, `QuantitySearchParam`, `UriSearchParam`, `ReferenceSearchParam`, `CompositeSearchParam`
              - `ClaimType` and `CompartmentType` for security and compartment searches
            - Supports schema migration via `SchemaUpgradeRunner`
            - Uses `RetrySqlCommandWrapper` (from Healthcare Shared Components) for transient fault handling

            ### 3. Cosmos DB Provider (`Microsoft.Health.Fhir.CosmosDb`)
            - Stores resources as documents in a single container
            - Uses a custom `FhirCosmosClient` with retry and circuit-breaker policies
            - Search is implemented via Cosmos DB SQL queries against indexed properties
            - Supports point reads for ID-based lookups (fastest path)

            ### 4. Transaction Support
            - SQL: Full ACID via `TransactionScope` or explicit `BEGIN TRANSACTION`
            - Cosmos: Uses stored-procedure-based batching for multi-document transactions

            ### Key Facts
            - **Schema Versioning**: The SQL provider supports seamless zero-downtime schema upgrades
            - **Dual Back-end**: You can switch between SQL and Cosmos at deployment time via configuration
            - **Optimistic Concurrency**: Both providers use ETag / `x-ms-version` checks to prevent lost updates
            """;
    }

    private static string BuildAuthArchitectureResponse()
    {
        return """
            ## Authentication & Authorisation Architecture (SMART on FHIR)

            The FHIR Server integrates with Azure Active Directory (AAD) and supports the SMART on FHIR framework.

            ### 1. Token Validation (`AadSmartOnFhirProxyAttribute` / `FhirAuthorizationService`)
            - Validates JWT bearer tokens from AAD
            - Extracts `roles`, `scope`, and `fhirUser` claims
            - Supports both v1.0 and v2.0 AAD tokens

            ### 2. SMART Scopes
            - Granular scopes: `patient/*.read`, `patient/Observation.write`, `user/Practitioner.read`, `system/*.*`
            - Scope restrictions are enforced in `FhirAuthorizationService`

            ### 3. Resource-Level Access Control
            - Compartment-based searches restrict results to the authorised patient compartment
            - Implemented in `CompartmentSearchExpressionBuilder`

            ### 4. Audit Logging
            - Every request is logged to an `IAuditLogger`
            - Includes caller identity, resource accessed, operation, and outcome
            - Supports export to Azure Monitor, Event Hubs, or custom sinks

            ### Key Facts
            - **SMART App Launch**: Supports both EHR launch and standalone launch flows
            - **Token Refresh**: Long-running operations like `$export` handle token refresh automatically
            - **AAD B2C**: Compatible with Azure AD B2C for patient-facing applications
            """;
    }

    private static string BuildGeneralArchitectureResponse()
    {
        return """
            ## Microsoft FHIR Server – High-Level Architecture

            The server is built as a modular ASP.NET Core application with the following request pipeline:

            ```
            HTTP Request
              -> Exception Handling Middleware
              -> Authentication Middleware (AAD / JWT validation)
              -> SMART Scope Authorisation
              -> FHIR Controller (MVC)
                 -> Model Binding (Hl7.Fhir.Model)
                 -> Mediator Request
                    -> Handler (CRUD, Search, Operation)
                       -> Data Store Abstraction (IFhirDataStore)
                          -> SQL Server OR Cosmos DB
                    -> Response Bundle
              -> FHIR Serialisation (JSON / XML)
              -> Audit Logging
            ```

            ### Key Design Principles
            1. **Modularity**: Each feature (CRUD, Search, Operations, Export) is isolated in its own assembly
            2. **Testability**: Heavy use of interfaces and MediatR enables comprehensive unit testing
            3. **Extensibility**: Custom operations and search parameters can be added without recompiling
            4. **Observability**: OpenTelemetry instrumentation, health checks, and structured logging throughout

            ### Major Components
            | Component | Responsibility |
            |-----------|---------------|
            | `FhirController` | MVC entry point for all FHIR interactions |
            | `Mediator` | Dispatches requests to handlers (MediatR) |
            | `IFhirDataStore` | Abstracts persistence |
            | `ISearchService` | Abstracts search (SQL or Cosmos) |
            | `IClaimsService` | Resolves identity and SMART scopes |
            | `IAuditLogger` | Records operational audit events |
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

        sb.AppendLine("Would you like me to explain how any of these files work, or search for something more specific?");
        return sb.ToString();
    }

    // ─── PR Guidance Response Builder ────────────────────────────────────────

    private static string BuildPrGuidanceResponse(string lowered)
    {
        if (lowered.Contains("search") || lowered.Contains("parameter"))
        {
            return """
                ## PR Guidance: Adding or Modifying Search Parameters

                Follow these steps to create a high-quality PR for search-parameter changes:

                ### 1. Branch Naming
                ```
                feature/search-param-[short-description]
                ```
                Example: `feature/search-param-patient-telecom-system`

                ### 2. Implementation Checklist
                - [ ] Update `SearchParameterRegistry` if adding a built-in parameter
                - [ ] Add SQL migration script in `Features/Schema/Migrations` if the new parameter requires a new search table column
                - [ ] Add unit tests in `SearchParameterRegistryTests.cs`
                - [ ] Add integration tests in `SearchTests.cs` for both SQL and Cosmos
                - [ ] Update documentation in `docs/Search.md`

                ### 3. Testing Requirements
                - **Unit tests**: Validate parameter parsing, validation, and registry lookups
                - **Integration tests**: Run search queries against an in-memory or containerised database
                - **Conformance tests**: Run the official HL7 FHIR R4 test suite against your branch
                - **Performance tests**: For parameters on large tables, verify query execution plans

                ### 4. PR Description Template
                ```markdown
                ## Description
                Brief description of the search parameter change.

                ## Related issues
                Addresses #issue-number.

                ## Testing
                Describe how this change was tested.

                ## SQL Migration
                Note any schema changes and their rollback strategy.
                ```

                ### 5. Reviewers
                Tag `@microsoft/fhir-server-maintainers` and at least one area expert.
                """;
        }

        if (lowered.Contains("data layer") || lowered.Contains("database") || lowered.Contains("cosmos") || lowered.Contains("sql"))
        {
            return """
                ## PR Guidance: Data Layer Changes

                Data layer PRs require extra scrutiny because they affect all downstream operations.

                ### 1. Branch Naming
                ```
                    feature/data-layer-[short-description]
                ```
                Example: `feature/data-layer-cosmos-batch-write`

                ### 2. Implementation Checklist
                - [ ] Both SQL and Cosmos providers updated (or justification provided for single-provider change)
                - [ ] Schema version bumped in `SchemaVersionConstants.cs`
                - [ ] Migration script written and tested (SQL)
                - [ ] Unit tests for the new repository method
                - [ ] Integration tests against SQL LocalDB and Cosmos Emulator
                - [ ] Backward-compatibility verified (existing data must not break)

                ### 3. Testing Requirements
                - **Unit tests**: Mock `IFhirDataStore` and assert correct method calls
                - **Integration tests**: Use `FhirStorageTestsFixture` for end-to-end CRUD
                - **Migration tests**: Apply migration to a copy of production-sized data and measure duration
                - **Rollback test**: Verify `Down()` migration (SQL) or document rollback procedure

                ### 4. PR Description Template
                ```markdown
                ## Description
                What changed and why.

                ## Schema Changes
                Any new tables, columns, or indexes.

                ## Breaking Changes
                Will existing deployments need manual intervention?

                ## Testing
                How was this tested at scale?
                ```
                """;
        }

        return """
            ## General PR Guidance for microsoft/fhir-server

            ### 1. Preparation
            - Fork the repository and create a feature branch from `main`
            - Ensure your branch is up-to-date: `git pull upstream main`

            ### 2. Branch Naming Conventions
            | Prefix | Use For | Example |
            |--------|---------|---------|
            | `feature/` | New capabilities | `feature/custom-search-param` |
            | `bugfix/` | Bug fixes | `bugfix/export-null-ref` |
            | `hotfix/` | Urgent production fixes | `hotfix/security-token-validation` |
            | `refactor/` | Code restructuring | `refactor/search-service-cleanup` |

            ### 3. Coding Standards
            - Follow the existing `.editorconfig` rules
            - Use file-scoped namespaces (C# 10+)
            - Add XML documentation comments for public APIs
            - Keep methods focused and under 60 lines when possible

            ### 4. Required Tests
            - Unit tests for new business logic
            - Integration tests for data-layer changes
            - E2E tests for API surface changes
            - Conformance tests for FHIR spec changes

            ### 5. PR Checklist
            - [ ] Build passes locally (`dotnet build`)
            - [ ] All tests pass (`dotnet test`)
            - [ ] Code style compliance (`dotnet format`)
            - [ ] PR description filled out with template
            - [ ] Linked related issues
            - [ ] Added/updated documentation

            ### 6. After Submission
            - CI will run the full test matrix (Windows + Linux, SQL + Cosmos)
            - Address reviewer feedback promptly
            - Squash commits before merge if requested
            """;
    }

    // ─── Fallback Response Builder ───────────────────────────────────────────

    private static string BuildFallbackResponse(string message)
    {
        return $"""
            I'm the **FHIR Server Expert**, and I specialise in the `microsoft/fhir-server` repository. I can help you with:

            - **Architecture** – How search, export, auth, or the data layer works
            - **Code search** – Find classes, methods, or configuration files
            - **PR guidance** – Step-by-step help for contributing pull requests

            Here are some key facts about the FHIR Server:
            - **FHIR R4** – Full compliance with the HL7 FHIR Release 4 specification
            - **Custom Search Parameters** – Runtime registration via the `SearchParameter` resource
            - **Dual Back-ends** – Supports both **SQL Server** (T-SQL) and **Azure Cosmos DB**
            - **$export Operation** – Bulk data export to NDJSON, parallelised, with optional anonymisation
            - **SMART on FHIR** – Token validation, granular scopes, and compartment-level access control
            - **Schema Versioning** – Zero-downtime SQL schema migrations
            - **OpenTelemetry** – Full observability with distributed tracing and metrics

            Your question was: "{message}"

            Could you clarify whether you're asking about **architecture**, **code**, or **PR guidance**? Or try asking one of the example questions from my skills list!
            """;
    }
}
