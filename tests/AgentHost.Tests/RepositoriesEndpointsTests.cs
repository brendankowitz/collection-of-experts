using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentHost;
using AgentHost.Indexing.Storage;
using AgentHost.Repositories.Registry;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace AgentHost.Tests;

public sealed class RepositoriesEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMetadataStore>();
            services.AddSingleton<IMetadataStore, InMemoryMetadataStore>();
        });
    });

    private HttpClient CreateClient() => _factory.CreateClient();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RepositoryCrudEndpoints_CreateListAndIndexRepository()
    {
        using var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/repositories", new
        {
            ownerOrOrg = "octo-org",
            name = "phase3-sample",
            source = "github",
            defaultBranch = "main",
            cloneUrl = "https://github.com/octo-org/phase3-sample.git",
            agentPersona = "Phase 3 Sample Expert",
            enabled = true
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        id.Should().NotBeNullOrWhiteSpace();

        var listResponse = await client.GetAsync("/api/repositories");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().Contain(item => item.GetProperty("id").GetString() == id);

        var indexResponse = await client.PostAsync($"/api/repositories/{id}/index", null);
        indexResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var payload = await indexResponse.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("jobId").GetString().Should().NotBeNullOrWhiteSpace();

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = scopeFactory.CreateScope();
        var metadataStore = scope.ServiceProvider.GetRequiredService<IMetadataStore>();
        var jobId = payload.GetProperty("jobId").GetGuid();
        var job = await metadataStore.GetJobAsync(jobId);
        job.Should().NotBeNull();
        job!.RepoId.Should().Be(id);
    }
}
