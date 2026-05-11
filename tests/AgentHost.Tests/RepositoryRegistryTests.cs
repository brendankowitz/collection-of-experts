using AgentHost.Repositories.Registry;
using FluentAssertions;
using Xunit;

namespace AgentHost.Tests;

public sealed class RepositoryRegistryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task InMemoryRegistry_SupportsCrudAndChangeEvents()
    {
        var registry = new InMemoryRepositoryRegistry();
        var changes = new List<RepositoryChange>();
        registry.Changed += (_, change) => changes.Add(change);

        var created = await registry.CreateAsync(new Repository
        {
            OwnerOrOrg = "octo-org",
            Name = "octo-repo",
            AgentPersona = "Octo Repo Expert",
            LanguageHints = ["csharp"]
        });

        created.Id.Should().NotBeNullOrWhiteSpace();
        (await registry.GetAsync(created.Id)).Should().NotBeNull();

        created.AgentPersona = "Updated Persona";
        var updated = await registry.UpdateAsync(created);
        updated.AgentPersona.Should().Be("Updated Persona");

        await registry.DeleteAsync(created.Id);
        (await registry.GetAsync(created.Id))!.Enabled.Should().BeFalse();

        await registry.DeleteAsync(created.Id, hard: true);
        (await registry.GetAsync(created.Id)).Should().BeNull();

        changes.Select(c => c.Kind).Should().Equal(
            RepositoryChangeKind.Added,
            RepositoryChangeKind.Updated,
            RepositoryChangeKind.Updated,
            RepositoryChangeKind.Removed);
    }
}
