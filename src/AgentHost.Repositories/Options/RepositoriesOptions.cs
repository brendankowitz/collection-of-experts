namespace AgentHost.Repositories.Options;

public sealed class RepositoriesOptions
{
    public const string Section = "Repositories";
    public string Backend { get; set; } = "InMemory";
    public string ConnectionString { get; set; } = "Host=localhost;Database=experts;Username=experts;Password=experts";
    public SourcesOptions Sources { get; set; } = new();
    public SeedRepository[] Seed { get; set; } = [];
}

public sealed class SourcesOptions
{
    public GitHubSourceOptions GitHub { get; set; } = new();
    public AzureDevOpsSourceOptions AzureDevOps { get; set; } = new();
}

public sealed class GitHubSourceOptions
{
    public string AuthMode { get; set; } = "Pat";
    public string? Pat { get; set; }
}

public sealed class AzureDevOpsSourceOptions
{
    public string? Pat { get; set; }
    public string? Organization { get; set; }
}

public sealed class SeedRepository
{
    public string Source { get; set; } = "github";
    public string OwnerOrOrg { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AgentPersona { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
