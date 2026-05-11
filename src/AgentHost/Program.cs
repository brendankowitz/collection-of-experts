using AgentHost.A2A;
using AgentHost.Agents;
using AgentHost.Hubs;
using AgentHost.Llm;
using AgentHost.MCP;
using AgentHost.Services;

namespace AgentHost;

/// <summary>
/// Entry point for the AgentHost ASP.NET Core application.
/// Configures the multi-agent system with A2A protocol endpoints,
/// SignalR real-time chat, MCP tools, and Swagger/OpenAPI documentation.
/// </summary>
public partial class Program
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();
        ConfigureMiddleware(app);

        app.Run();
    }

    /// <summary>
    /// Registers all application services with the DI container.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Core A2A services
        services.AddSingleton<AgentCardProvider>();
        services.AddSingleton<AgentTaskStore>();

        // Mock code index (shared by agents)
        services.AddSingleton<MockCodeIndexService>();
        services.AddAgentHostLlm(configuration);

        // Expert agents
        services.AddSingleton<FhirServerAgent>();
        services.AddSingleton<HealthcareComponentsAgent>();
        services.AddSingleton<IExpertAgent>(sp => sp.GetRequiredService<FhirServerAgent>());
        services.AddSingleton<IExpertAgent>(sp => sp.GetRequiredService<HealthcareComponentsAgent>());

        // Agent registry (resolves all IExpertAgent registrations)
        services.AddSingleton<AgentRegistry>();

        // SignalR for real-time chat
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        // Controllers + API explorer (needed for Swagger)
        services.AddControllers();
        services.AddEndpointsApiExplorer();

        // Swagger / OpenAPI
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Expert Agents API",
                Version = "1.0.0",
                Description =
                    "Multi-Agent Expert System backend supporting A2A protocol (JSON-RPC 2.0), " +
                    "SignalR real-time chat, and MCP tool interfaces.",
                Contact = new Microsoft.OpenApi.Models.OpenApiContact
                {
                    Name = "Expert Agents Team",
                    Email = "experts@example.com"
                }
            });

            // Include XML documentation comments
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        // Health checks
        services.AddHealthChecks()
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("AgentHost is running"));

        // Logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
            logging.SetMinimumLevel(LogLevel.Information);
        });
    }

    /// <summary>
    /// Configures the HTTP request pipeline (middleware ordering).
    /// </summary>
    private static void ConfigureMiddleware(WebApplication app)
    {
        // Development-only middleware
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Expert Agents API v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "Expert Agents API";
            });
        }

        // CORS for React frontend
        app.UseCors(policy => policy
            .WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials() // Required for SignalR
        );

        // HTTPS redirection
        app.UseHttpsRedirection();

        // Routing
        app.UseRouting();

        // Authentication & authorisation (placeholders for future expansion)
        // app.UseAuthentication();
        // app.UseAuthorization();

        // Map endpoints
        app.MapHealthChecks("/health");
        app.MapControllers();

        // A2A protocol endpoints
        app.MapA2AEndpoints();

        // MCP tool endpoints
        app.MapMcpEndpoints();

        // SignalR hub
        app.MapHub<ChatHub>("/hub/chat");

        // Root redirect to Swagger
        app.MapGet("/", () => Results.Redirect("/swagger"));

        // Simple info endpoint
        app.MapGet("/api/info", () => Results.Ok(new
        {
            name = "Expert Agents API",
            version = "1.0.0",
            agents = new[]
            {
                new { id = "fhir-server-expert", name = "FHIR Server Expert", url = "http://localhost:5001" },
                new { id = "healthcare-components-expert", name = "Healthcare Shared Components Expert", url = "http://localhost:5002" }
            },
            protocols = new[] { "A2A (JSON-RPC 2.0)", "SignalR", "MCP" },
            endpoints = new
            {
                agentCard = "/.well-known/agent-card.json",
                sendTask = "/tasks/send",
                sendSubscribe = "/tasks/sendSubscribe",
                getTask = "/tasks/{taskId}",
                cancelTask = "/tasks/{taskId}/cancel",
                chatHub = "/hub/chat",
                mcpTools = "/mcp/tools",
                health = "/health",
                swagger = "/swagger"
            }
        }));

        // Log startup
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation(
                "AgentHost started. Swagger: {SwaggerUrl}, ChatHub: {HubUrl}, Health: {HealthUrl}",
                "http://localhost:5000/swagger",
                "http://localhost:5000/hub/chat",
                "http://localhost:5000/health");
        });
    }
}
