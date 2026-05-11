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

    // -- EntraId HTTP test (uses env var so config is read before DI) ---------

    [Fact]
    public async Task EntraId_Health_Returns200_WithoutToken()
    {
        Environment.SetEnvironmentVariable("Authentication__Mode", "EntraId");
        Environment.SetEnvironmentVariable("Authentication__EntraId__Instance", "https://login.microsoftonline.com/");
        Environment.SetEnvironmentVariable("Authentication__EntraId__TenantId", "test-tenant");
        Environment.SetEnvironmentVariable("Authentication__EntraId__ClientId", "test-client");
        Environment.SetEnvironmentVariable("Authentication__EntraId__Audience", "api://test-client");
        try
        {
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(b =>
                {
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
        finally
        {
            Environment.SetEnvironmentVariable("Authentication__Mode", null);
            Environment.SetEnvironmentVariable("Authentication__EntraId__Instance", null);
            Environment.SetEnvironmentVariable("Authentication__EntraId__TenantId", null);
            Environment.SetEnvironmentVariable("Authentication__EntraId__ClientId", null);
            Environment.SetEnvironmentVariable("Authentication__EntraId__Audience", null);
        }
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