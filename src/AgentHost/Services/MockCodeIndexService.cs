using System.Collections.Concurrent;

namespace AgentHost.Services;

/// <summary>
/// Mock in-memory code index that maps file paths to content snippets
/// for both demo repositories. Provides simple keyword-based search.
/// </summary>
public class MockCodeIndexService
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _repos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the mock code index and seeds it with realistic file content
    /// for both repositories.
    /// </summary>
    public MockCodeIndexService()
    {
        SeedFhirServerRepo();
        SeedHealthcareComponentsRepo();
    }

    /// <summary>
    /// Searches the specified repository for files whose path or content
    /// contains the query keywords.
    /// </summary>
    /// <param name="repo">Repository name: <c>fhir-server</c> or <c>healthcare-shared-components</c>.</param>
    /// <param name="query">Free-text query.</param>
    /// <returns>Matching file paths and their content snippets.</returns>
    public List<(string FilePath, string Snippet)> Search(string repo, string query)
    {
        var results = new List<(string, string)>();

        if (!_repos.TryGetValue(repo, out var files))
            return results;

        var keywords = query
            .ToLowerInvariant()
            .Split(new[] { ' ', '.', '/', '-', '_', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(k => k.Length > 2)
            .ToList();

        foreach (var (path, content) in files)
        {
            var pathLower = path.ToLowerInvariant();
            var contentLower = content.ToLowerInvariant();

            bool matches = keywords.Any(k => pathLower.Contains(k) || contentLower.Contains(k));

            if (matches)
                results.Add((path, content));
        }

        return results;
    }

    /// <summary>
    /// Retrieves the full content of a specific file.
    /// </summary>
    /// <param name="repo">Repository name.</param>
    /// <param name="filePath">File path within the repository.</param>
    /// <returns>File content, or <c>null</c> if not found.</returns>
    public string? GetFileContent(string repo, string filePath)
    {
        if (!_repos.TryGetValue(repo, out var files))
            return null;

        files.TryGetValue(filePath, out var content);
        return content;
    }

    /// <summary>
    /// Returns all file paths in the specified repository.
    /// </summary>
    public IEnumerable<string> GetFilePaths(string repo)
    {
        if (!_repos.TryGetValue(repo, out var files))
            return [];
        return files.Keys;
    }

    // ─── Seed Data ───────────────────────────────────────────────────────────

    private void SeedFhirServerRepo()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/Microsoft.Health.Fhir.Core/Features/Search/SearchParameterRegistry.cs"] = """
                public class SearchParameterRegistry : ISearchParameterRegistry
                {
                    private readonly ConcurrentDictionary<string, SearchParameterInfo> _parameters = new();
                    private readonly ISearchParameterDataStore _dataStore;
                    private readonly ILogger<SearchParameterRegistry> _logger;

                    public SearchParameterRegistry(ISearchParameterDataStore dataStore, ILogger<SearchParameterRegistry> logger)
                    {
                        _dataStore = dataStore;
                        _logger = logger;
                    }

                    public async Task InitializeAsync(CancellationToken ct = default)
                    {
                        var parameters = await _dataStore.GetSearchParametersAsync(ct);
                        foreach (var param in parameters)
                        {
                            _parameters[param.Code] = param;
                        }
                        _logger.LogInformation("Loaded {Count} search parameters", _parameters.Count);
                    }

                    public SearchParameterInfo? GetSearchParameter(string resourceType, string code)
                    {
                        var key = $"{resourceType}-{code}";
                        return _parameters.GetValueOrDefault(key);
                    }

                    public async Task AddOrUpdateAsync(SearchParameterInfo parameter, CancellationToken ct = default)
                    {
                        _parameters[parameter.Code] = parameter;
                        await _dataStore.UpsertAsync(parameter, ct);
                        _logger.LogInformation("Added/Updated search parameter {Code}", parameter.Code);
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.SqlServer/Features/Search/SqlSearchService.cs"] = """
                public class SqlSearchService : ISearchService
                {
                    private readonly ISqlConnectionFactory _connectionFactory;
                    private readonly ISearchParameterExpressionParser _expressionParser;
                    private readonly ILogger<SqlSearchService> _logger;

                    public SqlSearchService(
                        ISqlConnectionFactory connectionFactory,
                        ISearchParameterExpressionParser expressionParser,
                        ILogger<SqlSearchService> logger)
                    {
                        _connectionFactory = connectionFactory;
                        _expressionParser = expressionParser;
                        _logger = logger;
                    }

                    public async Task<SearchResult> SearchAsync(
                        string resourceType,
                        IReadOnlyList<Tuple<string, string>> queryParams,
                        CancellationToken ct = default)
                    {
                        await using var connection = await _connectionFactory.GetConnectionAsync(ct);
                        var expression = _expressionParser.Parse(resourceType, queryParams);
                        var sqlExpression = expression.AcceptVisitor(new SqlExpressionVisitor());

                        var command = new SqlCommand(sqlExpression.ToString(), connection);
                        foreach (var param in sqlExpression.Parameters)
                        {
                            command.Parameters.AddWithValue(param.Name, param.Value);
                        }

                        await using var reader = await command.ExecuteReaderAsync(ct);
                        var resources = new List<ResourceWrapper>();
                        while (await reader.ReadAsync(ct))
                        {
                            resources.Add(ReadResourceWrapper(reader));
                        }

                        return new SearchResult(resources, continuationToken: null);
                    }

                    private static ResourceWrapper ReadResourceWrapper(SqlDataReader reader)
                    {
                        return new ResourceWrapper
                        {
                            ResourceId = reader.GetString(0),
                            Version = reader.GetInt32(1),
                            ResourceTypeName = reader.GetString(2),
                            RawResource = reader.GetString(3),
                            IsDeleted = reader.GetBoolean(4),
                            LastModified = reader.GetDateTimeOffset(5)
                        };
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.CosmosDb/Features/Search/CosmosSearchService.cs"] = """
                public class CosmosSearchService : ISearchService
                {
                    private readonly ICosmosDocumentQueryFactory _queryFactory;
                    private readonly ILogger<CosmosSearchService> _logger;

                    public CosmosSearchService(ICosmosDocumentQueryFactory queryFactory, ILogger<CosmosSearchService> logger)
                    {
                        _queryFactory = queryFactory;
                        _logger = logger;
                    }

                    public async Task<SearchResult> SearchAsync(
                        string resourceType,
                        IReadOnlyList<Tuple<string, string>> queryParams,
                        CancellationToken ct = default)
                    {
                        var query = new CosmosQueryBuilder(resourceType, queryParams).Build();
                        _logger.LogDebug("Cosmos DB query: {Query}", query.ToSqlQuery());

                        var documentQuery = _queryFactory.Create<DocumentWrapper>(query);
                        var feedResponse = await documentQuery.ExecuteNextAsync<DocumentWrapper>(ct);

                        var resources = feedResponse
                            .Select(d => new ResourceWrapper
                            {
                                ResourceId = d.Id,
                                ResourceTypeName = d.ResourceTypeName,
                                RawResource = d.RawResource,
                                LastModified = d.LastModified,
                                Version = d.Version
                            })
                            .ToList();

                        var continuationToken = feedResponse.ResponseContinuation;
                        return new SearchResult(resources, continuationToken);
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Api/Controllers/FhirController.cs"] = """
                [ApiController]
                [Route("fhir")]
                [ServiceFilter(typeof(AadSmartOnFhirProxyAttribute))]
                public class FhirController : ControllerBase
                {
                    private readonly IMediator _mediator;
                    private readonly ILogger<FhirController> _logger;

                    public FhirController(IMediator mediator, ILogger<FhirController> logger)
                    {
                        _mediator = mediator;
                        _logger = logger;
                    }

                    [HttpGet("{type}/{id}")]
                    public async Task<IActionResult> Read(string type, string id, CancellationToken ct)
                    {
                        _logger.LogInformation("Reading {ResourceType}/{ResourceId}", type, id);
                        var response = await _mediator.Send(new GetResourceRequest(type, id), ct);
                        return Ok(response.Resource);
                    }

                    [HttpPost("{type}")]
                    public async Task<IActionResult> Create(string type, [FromBody] Resource resource, CancellationToken ct)
                    {
                        _logger.LogInformation("Creating {ResourceType}", type);
                        var response = await _mediator.Send(new CreateResourceRequest(type, resource), ct);
                        return CreatedAtAction(nameof(Read), new { type, id = response.Resource.Id }, response.Resource);
                    }

                    [HttpGet("{type}")]
                    public async Task<IActionResult> Search(string type, CancellationToken ct)
                    {
                        var queryParams = Request.Query.Select(q => Tuple.Create(q.Key, q.Value.ToString())).ToList();
                        _logger.LogInformation("Searching {ResourceType} with {Count} parameters", type, queryParams.Count);
                        var response = await _mediator.Send(new SearchResourceRequest(type, queryParams), ct);
                        return Ok(response.Bundle);
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Core/Features/Operations/Export/ExportJobWorker.cs"] = """
                public class ExportJobWorker : BackgroundService
                {
                    private readonly IOperationDataStore _operationDataStore;
                    private readonly IExportJobFactory _jobFactory;
                    private readonly ILogger<ExportJobWorker> _logger;
                    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(10);

                    public ExportJobWorker(
                        IOperationDataStore operationDataStore,
                        IExportJobFactory jobFactory,
                        ILogger<ExportJobWorker> logger)
                    {
                        _operationDataStore = operationDataStore;
                        _jobFactory = jobFactory;
                        _logger = logger;
                    }

                    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                    {
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            try
                            {
                                var jobs = await _operationDataStore.AcquireExportJobsAsync(
                                    maximumNumberOfConcurrentJobsAllowed: 2,
                                    jobHeartbeatTimeoutThreshold: TimeSpan.FromMinutes(5),
                                    cancellationToken: stoppingToken);

                                foreach (var jobRecord in jobs)
                                {
                                    _logger.LogInformation("Starting export job {JobId}", jobRecord.Id);
                                    var processingJob = _jobFactory.Create(jobRecord);
                                    _ = Task.Run(() => processingJob.ExecuteAsync(stoppingToken), stoppingToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error polling for export jobs");
                            }

                            await Task.Delay(_pollingInterval, stoppingToken);
                        }
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Core/Features/Operations/Export/ExportProcessingJob.cs"] = """
                public class ExportProcessingJob : IJob
                {
                    private readonly IFhirDataStore _dataStore;
                    private readonly IExportDestinationClient _exportDestinationClient;
                    private readonly ILogger<ExportProcessingJob> _logger;

                    public ExportProcessingJob(
                        IFhirDataStore dataStore,
                        IExportDestinationClient exportDestinationClient,
                        ILogger<ExportProcessingJob> logger)
                    {
                        _dataStore = dataStore;
                        _exportDestinationClient = exportDestinationClient;
                        _logger = logger;
                    }

                    public async Task ExecuteAsync(CancellationToken ct)
                    {
                        foreach (var resourceType in _jobRecord.ResourceTypes)
                        {
                            var searchResult = await _dataStore.SearchAsync(new SearchOptions
                            {
                                ResourceTypes = new[] { resourceType },
                                Since = _jobRecord.Since,
                                MaxItemCount = 1000
                            }, ct);

                            var ndjsonFile = new NdjsonFile(resourceType);
                            foreach (var resource in searchResult.Results)
                            {
                                ndjsonFile.WriteLine(resource.RawResource);
                            }

                            await _exportDestinationClient.WriteFileAsync(
                                $"{_jobRecord.ExportFilePrefix}/{resourceType}.ndjson",
                                ndjsonFile.ToStream(), ct);

                            _logger.LogInformation("Exported {Count} {ResourceType} resources",
                                searchResult.Results.Count(), resourceType);
                        }
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Api/Features/Security/AadSmartOnFhirProxyAttribute.cs"] = """
                public class AadSmartOnFhirProxyAttribute : ActionFilterAttribute
                {
                    private readonly IFhirAuthorizationService _authorizationService;
                    private readonly ILogger<AadSmartOnFhirProxyAttribute> _logger;

                    public AadSmartOnFhirProxyAttribute(
                        IFhirAuthorizationService authorizationService,
                        ILogger<AadSmartOnFhirProxyAttribute> logger)
                    {
                        _authorizationService = authorizationService;
                        _logger = logger;
                    }

                    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
                    {
                        var httpContext = context.HttpContext;
                        var authorizationHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();

                        if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                        {
                            context.Result = new UnauthorizedResult();
                            return;
                        }

                        var token = authorizationHeader.Substring("Bearer ".Length);
                        var authorizationResult = await _authorizationService.AuthorizeAsync(
                            token,
                            httpContext.Request.Path,
                            httpContext.Request.Method,
                            httpContext.RequestAborted);

                        if (!authorizationResult.Succeeded)
                        {
                            _logger.LogWarning("Authorization failed: {FailureReason}", authorizationResult.FailureReason);
                            context.Result = new ForbidResult();
                            return;
                        }

                        httpContext.User = authorizationResult.Principal;
                        await next();
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Core/Features/Context/FhirRequestContext.cs"] = """
                public class FhirRequestContext : IFhirRequestContext
                {
                    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
                    public string ResourceType { get; set; }
                    public string ResourceId { get; set; }
                    public Bundle.RequestComponent Request { get; set; }
                    public ClaimsPrincipal Principal { get; set; }
                    public IList<OperationOutcomeIssue> Issues { get; set; } = new List<OperationOutcomeIssue>();
                    public bool IncludePartiallyIndexedSearchParams { get; set; }
                    public string CompartmentResourceType { get; set; }
                    public string CompartmentId { get; set; }
                    public IList<string> AccessControlContext { get; set; } = new List<string>();
                    public Uri BaseUri { get; set; }
                    public bool IsBackgroundTask { get; set; }
                }
                """,

            ["src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations/47.sql"] = """
                -- Schema version 47: Add index on TokenSearchParam for faster compartment searches

                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_TokenSearchParam_Code_SystemId' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
                BEGIN
                    CREATE NONCLUSTERED INDEX IX_TokenSearchParam_Code_SystemId
                    ON dbo.TokenSearchParam (Code, SystemId)
                    INCLUDE (ResourceSurrogateId)
                    WITH (ONLINE = ON, DROP_EXISTING = OFF);
                END

                IF EXISTS (SELECT * FROM dbo.SchemaVersion WHERE Version = 46)
                BEGIN
                    INSERT INTO dbo.SchemaVersion (Version, Status, LastUpdated)
                    VALUES (47, 'completed', GETUTCDATE());
                END
                """,

            ["src/Microsoft.Health.Fhir.Core/Features/Conformance/CapabilityStatementBuilder.cs"] = """
                public class CapabilityStatementBuilder
                {
                    private readonly IModelInfoProvider _modelInfoProvider;
                    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
                    private readonly ILogger<CapabilityStatementBuilder> _logger;

                    public CapabilityStatementBuilder(
                        IModelInfoProvider modelInfoProvider,
                        ISearchParameterDefinitionManager searchParameterDefinitionManager,
                        ILogger<CapabilityStatementBuilder> logger)
                    {
                        _modelInfoProvider = modelInfoProvider;
                        _searchParameterDefinitionManager = searchParameterDefinitionManager;
                        _logger = logger;
                    }

                    public CapabilityStatement Build()
                    {
                        var statement = new CapabilityStatement
                        {
                            Status = PublicationStatus.Active,
                            Kind = CapabilityStatementKind.Instance,
                            Date = DateTime.UtcNow.ToString("O"),
                            FhirVersion = FHIRVersion.N4_0_1,
                            Software = new CapabilityStatement.SoftwareComponent
                            {
                                Name = "Microsoft FHIR Server",
                                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                            },
                            Rest = new List<CapabilityStatement.RestComponent>
                            {
                                new CapabilityStatement.RestComponent
                                {
                                    Mode = RestfulCapabilityMode.Server,
                                    Resource = BuildResourceComponents()
                                }
                            }
                        };

                        _logger.LogInformation("Built capability statement with {ResourceCount} resources",
                            statement.Rest[0].Resource.Count);
                        return statement;
                    }

                    private List<CapabilityStatement.ResourceComponent> BuildResourceComponents()
                    {
                        var resourceTypes = _modelInfoProvider.GetResourceTypeNames();
                        return resourceTypes.Select(type => new CapabilityStatement.ResourceComponent
                        {
                            Type = type,
                            Interaction = GetInteractions(type),
                            SearchParam = _searchParameterDefinitionManager.GetSearchParameters(type)
                                .Select(sp => new CapabilityStatement.SearchParamComponent
                                {
                                    Name = sp.Code,
                                    Type = sp.Type
                                }).ToList()
                        }).ToList();
                    }

                    private static List<CapabilityStatement.ResourceInteractionComponent> GetInteractions(string resourceType)
                    {
                        var interactions = new List<CapabilityStatement.ResourceInteractionComponent>
                        {
                            new() { Code = TypeRestfulInteraction.Create },
                            new() { Code = TypeRestfulInteraction.Read },
                            new() { Code = TypeRestfulInteraction.Update },
                            new() { Code = TypeRestfulInteraction.Delete },
                            new() { Code = TypeRestfulInteraction.SearchType },
                            new() { Code = TypeRestfulInteraction.Vread },
                            new() { Code = TypeRestfulInteraction.HistoryInstance },
                            new() { Code = TypeRestfulInteraction.HistoryType }
                        };
                        return interactions;
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Core/Features/Operations/ConvertData/ConvertDataEngine.cs"] = """
                public class ConvertDataEngine : IConvertDataEngine
                {
                    private readonly ITemplateProvider _templateProvider;
                    private readonly ILogger<ConvertDataEngine> _logger;

                    public ConvertDataEngine(ITemplateProvider templateProvider, ILogger<ConvertDataEngine> logger)
                    {
                        _templateProvider = templateProvider;
                        _logger = logger;
                    }

                    public async Task<Resource> ConvertAsync(ConvertDataRequest request, CancellationToken ct)
                    {
                        _logger.LogInformation("Converting {InputType} using template {TemplateName}",
                            request.InputDataType, request.TemplateReference);

                        var template = await _templateProvider.GetTemplateAsync(request.TemplateReference, ct);
                        var inputObject = JsonConvert.DeserializeObject(request.InputData);
                        var result = template.Transform(inputObject);

                        return FhirJsonNode.Parse(result.ToString()).ToPoco<Resource>();
                    }
                }
                """,

            ["src/Microsoft.Health.Fhir.Shared.Web/Startup.cs"] = """
                public class Startup
                {
                    public IConfiguration Configuration { get; }

                    public Startup(IConfiguration configuration)
                    {
                        Configuration = configuration;
                    }

                    public void ConfigureServices(IServiceCollection services)
                    {
                        services.AddFhirServer(Configuration)
                            .AddSqlServer(Configuration)
                            .AddCosmosDb(Configuration)
                            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Startup>())
                            .AddHealthChecks()
                            .AddSqlServer(Configuration)
                            .AddCosmosDb(Configuration);

                        services.AddControllers()
                            .AddNewtonsoftJson()
                            .AddFhirFormatters();

                        services.AddOpenTelemetry()
                            .WithTracing(builder => builder.AddAspNetCoreInstrumentation());
                    }

                    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
                    {
                        app.UseExceptionHandlingMiddleware();
                        app.UseHttpsRedirection();
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseAudit();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                            endpoints.MapHealthChecks("/health");
                        });
                    }
                }
                """
        };

        _repos["fhir-server"] = files;
    }

    private void SeedHealthcareComponentsRepo()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/Microsoft.Health.SqlServer/Features/Storage/RetrySqlConnectionWrapper.cs"] = """
                public class RetrySqlConnectionWrapper : SqlConnectionWrapper
                {
                    private readonly SqlServerDataStoreConfiguration _configuration;
                    private readonly ILogger<RetrySqlConnectionWrapper> _logger;

                    private static readonly int[] TransientErrorNumbers =
                    {
                        -2,   // Timeout
                        20,   // Encryption error
                        64,   // Named pipe error
                        233,  // Connection initialization error
                        1205, // Deadlock
                        1222, // Lock timeout
                        4060, // Cannot open database
                        40197, // Service error
                        40501, // Service busy
                        40613  // Database unavailable
                    };

                    public RetrySqlConnectionWrapper(
                        SqlServerDataStoreConfiguration configuration,
                        ILogger<RetrySqlConnectionWrapper> logger)
                    {
                        _configuration = configuration;
                        _logger = logger;
                    }

                    public override async Task OpenAsync(CancellationToken ct = default)
                    {
                        var retryCount = 0;
                        var delay = _configuration.Retry.InitialDelay;

                        while (true)
                        {
                            try
                            {
                                await base.OpenAsync(ct);
                                return;
                            }
                            catch (SqlException ex) when (IsTransient(ex))
                            {
                                retryCount++;
                                if (retryCount > _configuration.Retry.MaxRetries)
                                    throw;

                                _logger.LogWarning(ex, "Transient SQL error {ErrorNumber}, retry {RetryCount}/{MaxRetries}",
                                    ex.Number, retryCount, _configuration.Retry.MaxRetries);

                                await Task.Delay(delay, ct);
                                delay = TimeSpan.FromTicks(Math.Min(
                                    delay.Ticks * _configuration.Retry.BackoffMultiplier,
                                    _configuration.Retry.MaxDelay.Ticks));
                            }
                        }
                    }

                    private static bool IsTransient(SqlException ex)
                    {
                        return TransientErrorNumbers.Contains(ex.Number);
                    }
                }
                """,

            ["src/Microsoft.Health.SqlServer/Features/Storage/SqlTransactionScope.cs"] = """
                public class SqlTransactionScope : IDisposable
                {
                    private readonly TransactionScope _transactionScope;
                    private readonly ILogger<SqlTransactionScope> _logger;
                    private bool _completed;

                    public SqlTransactionScope(
                        TimeSpan timeout,
                        IsolationLevel isolationLevel,
                        ILogger<SqlTransactionScope> logger)
                    {
                        _logger = logger;
                        var options = new TransactionOptions
                        {
                            Timeout = timeout,
                            IsolationLevel = isolationLevel
                        };
                        _transactionScope = new TransactionScope(TransactionScopeOption.Required, options);
                        _logger.LogDebug("Transaction scope created with isolation {IsolationLevel}", isolationLevel);
                    }

                    public static SqlTransactionScope BeginTransaction(
                        TimeSpan? timeout = null,
                        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
                    {
                        return new SqlTransactionScope(timeout ?? TimeSpan.FromMinutes(1), isolationLevel, null);
                    }

                    public void Complete()
                    {
                        _transactionScope.Complete();
                        _completed = true;
                        _logger?.LogDebug("Transaction completed successfully");
                    }

                    public void Dispose()
                    {
                        _transactionScope.Dispose();
                        if (!_completed)
                        {
                            _logger?.LogWarning("Transaction scope disposed without Complete() - rolling back");
                        }
                    }
                }
                """,

            ["src/Microsoft.Health.Blob/Features/Storage/BlobClient.cs"] = """
                public class BlobClient : IBlobClient
                {
                    private readonly Azure.Storage.Blobs.BlobServiceClient _blobServiceClient;
                    private readonly BlobDataStoreConfiguration _configuration;
                    private readonly ILogger<BlobClient> _logger;

                    public BlobClient(
                        BlobDataStoreConfiguration configuration,
                        ILogger<BlobClient> logger)
                    {
                        _configuration = configuration;
                        _logger = logger;

                        _blobServiceClient = string.IsNullOrEmpty(configuration.ConnectionString)
                            ? new BlobServiceClient(new Uri(configuration.ServiceUrl), new DefaultAzureCredential())
                            : new BlobServiceClient(configuration.ConnectionString);
                    }

                    public async Task<Stream> OpenReadAsync(string blobName, CancellationToken ct = default)
                    {
                        var containerClient = _blobServiceClient.GetBlobContainerClient(_configuration.ContainerName);
                        var blobClient = containerClient.GetBlobClient(blobName);
                        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
                        return response.Value.Content;
                    }

                    public async Task UploadAsync(string blobName, Stream content, CancellationToken ct = default)
                    {
                        var containerClient = _blobServiceClient.GetBlobContainerClient(_configuration.ContainerName);
                        var blobClient = containerClient.GetBlobClient(blobName);
                        await blobClient.UploadAsync(content, overwrite: true, ct);
                        _logger.LogInformation("Uploaded blob {BlobName}", blobName);
                    }

                    public async Task<bool> ExistsAsync(string blobName, CancellationToken ct = default)
                    {
                        var containerClient = _blobServiceClient.GetBlobContainerClient(_configuration.ContainerName);
                        var blobClient = containerClient.GetBlobClient(blobName);
                        return await blobClient.ExistsAsync(ct);
                    }

                    public async Task DeleteAsync(string blobName, CancellationToken ct = default)
                    {
                        var containerClient = _blobServiceClient.GetBlobContainerClient(_configuration.ContainerName);
                        var blobClient = containerClient.GetBlobClient(blobName);
                        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
                        _logger.LogInformation("Deleted blob {BlobName}", blobName);
                    }
                }
                """,

            ["src/Microsoft.Health.Core/Features/ChangeFeed/IChangeFeedSource.cs"] = """
                public interface IChangeFeedSource
                {
                    IAsyncEnumerable<ChangeFeedEntry> ReadChangesAsync(
                        ChangeFeedOptions options,
                        CancellationToken ct = default);
                }

                public class ChangeFeedEntry
                {
                    public string ResourceId { get; init; }
                    public string ResourceTypeName { get; init; }
                    public int Version { get; init; }
                    public DateTimeOffset LastModified { get; init; }
                    public ChangeType ChangeType { get; init; }
                }

                public enum ChangeType
                {
                    Create,
                    Update,
                    Delete
                }

                public class ChangeFeedOptions
                {
                    public string PartitionKey { get; set; }
                    public string ContinuationToken { get; set; }
                    public int? MaxItemCount { get; set; } = 100;
                    public TimeSpan? PollInterval { get; set; }
                }
                """,

            ["src/Microsoft.Health.SqlServer/Features/Health/SqlServerHealthCheck.cs"] = """
                public class SqlServerHealthCheck : IHealthCheck
                {
                    private readonly ISqlConnectionFactory _connectionFactory;
                    private readonly ILogger<SqlServerHealthCheck> _logger;

                    public SqlServerHealthCheck(
                        ISqlConnectionFactory connectionFactory,
                        ILogger<SqlServerHealthCheck> logger)
                    {
                        _connectionFactory = connectionFactory;
                        _logger = logger;
                    }

                    public async Task<HealthCheckResult> CheckHealthAsync(
                        HealthCheckContext context,
                        CancellationToken ct = default)
                    {
                        try
                        {
                            await using var connection = await _connectionFactory.GetConnectionAsync(ct);
                            await connection.ExecuteAsync("SELECT 1", ct);
                            return HealthCheckResult.Healthy("SQL Server is reachable");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SQL Server health check failed");
                            return HealthCheckResult.Unhealthy("SQL Server is not reachable", ex);
                        }
                    }
                }
                """,

            ["src/Microsoft.Health.Blob/Features/Health/BlobHealthCheck.cs"] = """
                public class BlobHealthCheck : IHealthCheck
                {
                    private readonly IBlobClient _blobClient;
                    private readonly ILogger<BlobHealthCheck> _logger;

                    public BlobHealthCheck(IBlobClient blobClient, ILogger<BlobHealthCheck> logger)
                    {
                        _blobClient = blobClient;
                        _logger = logger;
                    }

                    public async Task<HealthCheckResult> CheckHealthAsync(
                        HealthCheckContext context,
                        CancellationToken ct = default)
                    {
                        try
                        {
                            var testBlobName = $"healthcheck-{Guid.NewGuid()}.txt";
                            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("healthy"));
                            await _blobClient.UploadAsync(testBlobName, stream, ct);
                            await _blobClient.DeleteAsync(testBlobName, ct);
                            return HealthCheckResult.Healthy("Blob storage is accessible");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Blob storage health check failed");
                            return HealthCheckResult.Unhealthy("Blob storage is not accessible", ex);
                        }
                    }
                }
                """,

            ["src/Microsoft.Health.Api/Features/ExceptionHandling/ExceptionHandlingMiddleware.cs"] = """
                public class ExceptionHandlingMiddleware : IMiddleware
                {
                    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

                    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger)
                    {
                        _logger = logger;
                    }

                    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
                    {
                        try
                        {
                            await next(context);
                        }
                        catch (BadRequestException ex)
                        {
                            await WriteProblemDetailsAsync(context, StatusCodes.Status400BadRequest, ex.Message);
                        }
                        catch (UnauthorizedException ex)
                        {
                            await WriteProblemDetailsAsync(context, StatusCodes.Status401Unauthorized, ex.Message);
                        }
                        catch (ForbiddenException ex)
                        {
                            await WriteProblemDetailsAsync(context, StatusCodes.Status403Forbidden, ex.Message);
                        }
                        catch (ResourceNotFoundException ex)
                        {
                            await WriteProblemDetailsAsync(context, StatusCodes.Status404NotFound, ex.Message);
                        }
                        catch (ConflictException ex)
                        {
                            await WriteProblemDetailsAsync(context, StatusCodes.Status409Conflict, ex.Message);
                        }
                        catch (ServiceUnavailableException ex)
                        {
                            _logger.LogError(ex, "Service unavailable");
                            await WriteProblemDetailsAsync(context, StatusCodes.Status503ServiceUnavailable, "Service temporarily unavailable");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Unhandled exception");
                            await WriteProblemDetailsAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred");
                        }
                    }

                    private static async Task WriteProblemDetailsAsync(HttpContext context, int statusCode, string detail)
                    {
                        context.Response.StatusCode = statusCode;
                        context.Response.ContentType = "application/problem+json";
                        var problem = new ProblemDetails
                        {
                            Status = statusCode,
                            Detail = detail,
                            Instance = context.Request.Path
                        };
                        await context.Response.WriteAsJsonAsync(problem);
                    }
                }
                """,

            ["src/Microsoft.Health.SqlServer/Features/Schema/SchemaVersionConstants.cs"] = """
                public static class SchemaVersionConstants
                {
                    public const int Min = 1;
                    public const int Max = 47;
                    public const int Current = 47;

                    public static readonly List<int> SupportedVersions = Enumerable.Range(Min, Max - Min + 1).ToList();

                    public static bool IsSupported(int version)
                    {
                        return version >= Min && version <= Max;
                    }
                }
                """,

            ["src/Microsoft.Health.SqlServer/Configs/SqlServerDataStoreConfiguration.cs"] = """
                public class SqlServerDataStoreConfiguration
                {
                    public string ConnectionString { get; set; }
                    public int CommandTimeoutSeconds { get; set; } = 30;
                    public RetryConfiguration Retry { get; set; } = new RetryConfiguration();
                    public bool AllowDatabaseCreation { get; set; } = false;
                    public int MaxPoolSize { get; set; } = 100;
                    public int InitialPoolSize { get; set; } = 5;
                    public bool MultipleActiveResultSets { get; set; } = true;
                    public string ApplicationName { get; set; } = "Microsoft.Health.Fhir";
                }

                public class RetryConfiguration
                {
                    public int MaxRetries { get; set; } = 5;
                    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
                    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
                    public double BackoffMultiplier { get; set; } = 2.0;
                    public List<int> TransientErrorNumbers { get; set; } = new();
                }
                """,

            ["src/Microsoft.Health.Core/Exceptions/BadRequestException.cs"] = """
                public class BadRequestException : MicrosoftHealthException
                {
                    public BadRequestException(string message)
                        : base(message)
                    {
                    }

                    public BadRequestException(string message, Exception innerException)
                        : base(message, innerException)
                    {
                    }

                    public override HttpStatusCode StatusCode => HttpStatusCode.BadRequest;
                    public override string IssueType => "invalid";
                }
                """,

            ["src/Microsoft.Health.Core/Exceptions/ResourceNotFoundException.cs"] = """
                public class ResourceNotFoundException : MicrosoftHealthException
                {
                    public string ResourceType { get; }
                    public string ResourceId { get; }

                    public ResourceNotFoundException(string resourceType, string resourceId)
                        : base($"Resource {resourceType}/{resourceId} was not found.")
                    {
                        ResourceType = resourceType;
                        ResourceId = resourceId;
                    }

                    public override HttpStatusCode StatusCode => HttpStatusCode.NotFound;
                    public override string IssueType => "not-found";
                }
                """,

            ["src/Microsoft.Health.Core/Features/Security/RoleLoader.cs"] = """
                public class RoleLoader : IHostedService
                {
                    private readonly IAuthorizationPolicyProvider _policyProvider;
                    private readonly IDataStore _roleStore;
                    private readonly ILogger<RoleLoader> _logger;

                    public RoleLoader(
                        IAuthorizationPolicyProvider policyProvider,
                        IDataStore roleStore,
                        ILogger<RoleLoader> logger)
                    {
                        _policyProvider = policyProvider;
                        _roleStore = roleStore;
                        _logger = logger;
                    }

                    public async Task StartAsync(CancellationToken ct)
                    {
                        var roles = await _roleStore.GetRolesAsync(ct);
                        foreach (var role in roles)
                        {
                            var policy = new AuthorizationPolicyBuilder()
                                .RequireClaim(ClaimTypes.Role, role.Name)
                                .Build();

                            _policyProvider.AddPolicy(role.Name, policy);
                            _logger.LogInformation("Loaded role {RoleName} with {ClaimCount} claims", role.Name, role.Claims.Count);
                        }
                    }

                    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
                }
                """
        };

        _repos["healthcare-shared-components"] = files;
    }
}
