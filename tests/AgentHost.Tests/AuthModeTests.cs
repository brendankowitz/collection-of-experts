using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AgentHost.Tests;

/// <summary>
/// Phase 7 auth-mode smoke tests.
/// </summary>
public class AuthModeTests
{
    // -- Disabled mode -------------------------------------------------------

    [Fact]
    public async Task Disabled_Health_Returns200()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_Repositories_Returns200_WithoutToken()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/repositories");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -- ConfigureAuth DI tests (EntraId mode) --------------------------------

    [Fact]
    public async Task EntraId_ConfigureAuth_Registers_RepositoryAdmin_Policy_With_Role()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Mode"] = "EntraId",
                ["Authentication:EntraId:Instance"] = "https://login.microsoftonline.com/",
                ["Authentication:EntraId:TenantId"] = "test-tenant",
                ["Authentication:EntraId:ClientId"] = "test-client",
                ["Authentication:EntraId:Audience"] = "api://test-client"
            })
            .Build();

        Program.ConfigureAuth(services, config);

        var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();
        var policy = await authOptions.GetPolicyAsync("RepositoryAdmin");

        Assert.NotNull(policy);
        Assert.True(policy.Requirements.Count > 0);
    }

    [Fact]
    public async Task EntraId_ConfigureAuth_Registers_ChatUser_Policy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Mode"] = "EntraId",
                ["Authentication:EntraId:Instance"] = "https://login.microsoftonline.com/",
                ["Authentication:EntraId:TenantId"] = "test-tenant",
                ["Authentication:EntraId:ClientId"] = "test-client",
                ["Authentication:EntraId:Audience"] = "api://test-client"
            })
            .Build();

        Program.ConfigureAuth(services, config);

        var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();
        var policy = await authOptions.GetPolicyAsync("ChatUser");

        Assert.NotNull(policy);
        Assert.True(policy.Requirements.Count > 0);
    }

    [Fact]
    public async Task Disabled_ConfigureAuth_Registers_Anonymous_Policies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Mode"] = "Disabled" })
            .Build();

        Program.ConfigureAuth(services, config);

        var sp = services.BuildServiceProvider();
        var authOptions = sp.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();
        var repoPolicy = await authOptions.GetPolicyAsync("RepositoryAdmin");
        var chatPolicy = await authOptions.GetPolicyAsync("ChatUser");

        Assert.NotNull(repoPolicy);
        Assert.NotNull(chatPolicy);
    }

    // -- EntraId HTTP test ----------------------------------------------------

    [Fact]
    public async Task EntraId_Health_Returns200_WithoutToken()
    {
        // Use ConfigureAppConfiguration (not env vars) to avoid polluting the process-wide
        // environment for concurrently running test classes (e.g. McpEndpointTests).
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Authentication:Mode"] = "EntraId",
                        ["Authentication:EntraId:Instance"] = "https://login.microsoftonline.com/",
                        ["Authentication:EntraId:TenantId"] = "test-tenant",
                        ["Authentication:EntraId:ClientId"] = "test-client",
                        ["Authentication:EntraId:Audience"] = "api://test-client"
                    });
                });
                b.ConfigureServices(svc =>
                {
                    svc.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                    {
                        o.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = false,
                            ValidateAudience = false,
                            ValidateLifetime = false,
                            ValidateIssuerSigningKey = false,
                            SignatureValidator = (token, _) => new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token)
                        };
                        o.BackchannelHttpHandler = new AlwaysOkHandler();
                        o.Authority = "https://login.microsoftonline.com/test-tenant/v2.0";
                        o.RequireHttpsMetadata = false;
                    });
                });
            });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -- Disabled mode: /tasks/send and /hub/chat must not require auth --------

    [Fact]
    public async Task Disabled_TasksSend_Returns_Non401()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        // POST /tasks/send with a minimal A2A JSON-RPC body; we expect the server to process it
        // (even if the payload is invalid) — NOT to return 401/403.
        var body = new StringContent(
            """{"jsonrpc":"2.0","id":"1","method":"tasks/send","params":{"id":"t1","message":{"role":"user","parts":[{"type":"text","text":"ping"}]}}}""",
            System.Text.Encoding.UTF8,
            "application/json");
        var response = await client.PostAsync("/tasks/send", body);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -- SignalR bad-Redis smoke test -----------------------------------------

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task SignalR_BadRedis_AppStarts_WithoutException()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SignalR:Redis:ConnectionString"] = "localhost:16379,abortConnect=false"
                    });
                });
            });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -- Helpers -------------------------------------------------------------

    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
    }
}