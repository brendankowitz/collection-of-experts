namespace AgentHost.Repositories.Registry;

public sealed class Repository
{
    public string Id { get; set; } = string.Empty;
    public RepositorySource Source { get; set; }
    public string OwnerOrOrg { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string CloneUrl { get; set; } = string.Empty;
    public string? AuthSecretRef { get; set; }
    public string[] LanguageHints { get; set; } = [];
    public string AgentPersona { get; set; } = string.Empty;
    public Dictionary<string, string> PromptOverrides { get; set; } = new(StringComparer.Ordinal);
    public string? IndexingSchedule { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
