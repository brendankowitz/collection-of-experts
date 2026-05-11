using AgentHost.Llm.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentHost.Llm;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentHostLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddSingleton<ITokenAccountant, TokenAccountant>();
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        services.AddSingleton<PromptTemplateLibrary>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<PromptTemplateLibrary>>();
            var library = new PromptTemplateLibrary(logger);
            library.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "prompts"));
            return library;
        });

        return services;
    }
}
