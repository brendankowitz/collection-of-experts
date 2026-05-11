namespace AgentHost.Llm;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string DefaultProvider { get; set; } = "Mock";

    public string DefaultModel { get; set; } = "gpt-4o";

    public Dictionary<string, ProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, AgentOverrideOptions> AgentOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderOptions
{
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public Dictionary<string, string> Deployments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentOverrideOptions
{
    public string? Provider { get; set; }

    public string? Model { get; set; }
}
