namespace AgentHost.Repositories.Sources;

public sealed record RepositoryMetadata(
    string FullName,
    string DefaultBranch,
    string CloneUrl,
    string Description,
    bool IsPrivate);
