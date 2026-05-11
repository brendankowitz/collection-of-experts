using AgentHost.A2A;
using AgentHost.Agents;
using AgentHost.Hubs;
using AgentHost.Indexing;
using AgentHost.Llm;
using AgentHost.MCP;
using AgentHost.Repositories;
using AgentHost.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using ModelContextProtocol.Protocol;

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

    internal static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AgentCardProvider>();
        services.AddAgentHostPersistence(configuration);

        services.AddSingleton<MockCodeIndexService>();
        services.AddSingleton<MockCodeIndexServiceCompat>();
        services.AddAgentHostLlm(configuration);
        services.AddAgentHostIndexing(configuration);
        services.AddRepositoryRegistry(configuration);

        services.AddSingleton<AgentRegistry>();
        services.AddHostedService<DynamicAgentRegistry>();

        // -- Authentication & Authorization (Phase 7) --
        ConfigureAuth(services, configuration);

        services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "expert-agents",
                Version = "1.0.0"
            };
        })
        .WithHttpTransport(o => o.Stateless = true)
        .WithTools<ExpertAgentsMcpTools>();

        // -- SignalR with optional Redis backplane (Phase 7) --
        var signalR = services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 1024 * 1024;
        })
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

        var redisConnectionString = configuration["SignalR:Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            signalR.AddStackExchangeRedis(redisConnectionString, o =>
            {
                o.Configuration.AbortOnConnectFail = false;
            });
        }

        // -- Application Insights (Phase 7) --
        var appInsightsConnectionString = configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = appInsightsConnectionString;
            });
        }

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

    internal static void ConfigureAuth(IServiceCollection services, IConfiguration configuration)
    {
        var authMode = configuration["Authentication:Mode"] ?? "Disabled";

        if (string.Equals(authMode, "EntraId", StringComparison.OrdinalIgnoreCase))
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(configuration.GetSection("Authentication:EntraId"));

            services.AddAuthorization(options =>
            {
                options.AddPolicy("RepositoryAdmin", policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole("Repositories.Admin"));

                options.AddPolicy("ChatUser", policy => policy
                    .RequireAuthenticatedUser());
            });
        }
        else
        {
            // Disabled mode: allow anonymous for all policies
            services.AddAuthentication();
            services.AddAuthorization(options =>
            {
                options.AddPolicy("RepositoryAdmin", policy => policy.RequireAssertion(_ => true));
                options.AddPolicy("ChatUser", policy => policy.RequireAssertion(_ => true));
            });
        }
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
        app.UseAuthentication();
        app.UseAuthorization();

        var authMode = app.Configuration["Authentication:Mode"] ?? "Disabled";
        var isEntraId = string.Equals(authMode, "EntraId", StringComparison.OrdinalIgnoreCase);

        app.MapHealthChecks("/health");
        app.MapControllers();
        app.MapA2AEndpoints();

        // MCP endpoint with optional auth
        var mcpEndpoint = app.MapMcp("/mcp");
        if (isEntraId) mcpEndpoint.RequireAuthorization("ChatUser");

        app.MapGet("/mcp", async (HttpContext context, CancellationToken ct) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsync(": connected\n\n", ct);
            await context.Response.Body.FlushAsync(ct);

            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
            }
        })
        .WithName("McpSse")
        .ExcludeFromDescription();

        app.MapRepositoriesEndpoints();

        // SignalR hub with optional auth
        var hubEndpoint = app.MapHub<ChatHub>("/hub/chat");
        if (isEntraId) hubEndpoint.RequireAuthorization("ChatUser");

        app.MapGet("/", static () => Results.Redirect("/swagger"));

        app.MapGet("/mcp/health", (IServiceProvider sp) =>
        {
            const int toolCount = 9;
            return Results.Ok(new
            {
                status = "ok",
                protocolVersion = "2024-11-05",
                tools = toolCount
            });
        })
        .WithName("McpHealth")
        .ExcludeFromDescription();

        app.MapGet("/api/info", (AgentRegistry registry) => Results.Ok(new
        {
            name = "Expert Agents API",
            version = "1.0.0",
            agents = registry.GetAllAgents().Select(agent => new { id = agent.AgentId, name = agent.Name, url = "http://localhost:5000" }),
            protocols = new[] { "A2A (JSON-RPC 2.0)", "SignalR", "MCP (JSON-RPC 2.0 / Streamable HTTP)" },
            endpoints = new
            {
                agentCard = "/.well-known/agent-card.json",
                sendTask = "/tasks/send",
                sendSubscribe = "/tasks/sendSubscribe",
                getTask = "/tasks/{taskId}",
                cancelTask = "/tasks/{taskId}/cancel",
                chatHub = "/hub/chat",
                mcpJsonRpc = "/mcp",
                mcpHealth = "/mcp/health",
                repositories = "/api/repositories",
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