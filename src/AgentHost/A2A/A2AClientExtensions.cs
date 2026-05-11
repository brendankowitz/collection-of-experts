using AgentHost.Agents;
using AgentHost.Orchestration;

namespace AgentHost.A2A;

/// <summary>
/// DI registration helpers for the A2A client stack.
/// </summary>
public static class A2AClientExtensions
{
    /// <summary>
    /// Registers <see cref="IA2AClient"/>, its concrete implementations, and the named HttpClient
    /// used for outbound A2A calls.
    /// </summary>
    public static IServiceCollection AddA2AClient(this IServiceCollection services)
    {
        services.AddHttpClient("a2a", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        // Break circular dependency: AgentRegistry → CoordinatorAgent → IA2AClient → InProcessA2AClient → AgentRegistry
        services.AddSingleton(sp => new Lazy<AgentRegistry>(() => sp.GetRequiredService<AgentRegistry>()));
        services.AddSingleton<InProcessA2AClient>();
        services.AddSingleton<HttpA2AClient>();
        services.AddSingleton<CompositeA2AClient>();
        services.AddSingleton<IA2AClient>(sp => sp.GetRequiredService<CompositeA2AClient>());

        return services;
    }
}
