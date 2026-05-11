namespace AgentHost.Repositories.Registry;

public enum RepositoryChangeKind { Added, Updated, Removed }

public sealed record RepositoryChange(RepositoryChangeKind Kind, Repository Repository);
