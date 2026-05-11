using AgentHost.A2A;
using AgentHost.Agents;
using AgentHost.Hubs;
using AgentHost.Indexing;
using AgentHost.Llm;
using AgentHost.MCP;
using AgentHost.Services;

namespace AgentHost;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();
        ConfigureMiddleware(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AgentCardProvider>();
        services.AddSingleton<AgentTaskStore>();

        services.AddSingleton<MockCodeIndexService>();
        services.AddSingleton<MockCodeIndexServiceCompat>();
        services.AddAgentHostLlm(configuration);
        services.AddAgentHostIndexing(configuration);

        services.AddSingleton<FhirServerAgent>();
        services.AddSingleton<HealthcareComponentsAgent>();
        services.AddSingleton<IExpertAgent>(sp => sp.GetRequiredService<FhirServerAgent>());
        services.AddSingleton<IExpertAgent>(sp => sp.GetRequiredService<HealthcareComponentsAgent>());

        services.AddSingleton<AgentRegistry>();

        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 1024 * 1024;
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        services.AddControllers();
        services.AddEndpointsApiExplorer();

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

            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        services.AddHealthChecks()
            .AddCheck("self", static () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("AgentHost is running"));

        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
            logging.SetMinimumLevel(LogLevel.Information);
        });
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
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

        app.UseCors(policy => policy
            .WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());

        app.UseHttpsRedirection();
        app.UseRouting();

        app.MapHealthChecks("/health");
        app.MapControllers();
        app.MapA2AEndpoints();
        app.MapMcpEndpoints();
        app.MapHub<ChatHub>("/hub/chat");

        app.MapGet("/", static () => Results.Redirect("/swagger"));

        app.MapGet("/api/info", static () => Results.Ok(new
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
