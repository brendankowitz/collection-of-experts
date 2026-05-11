using AgentHost.Llm.Providers;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AgentHost.Llm;

public sealed class ChatClientFactory : IChatClientFactory
{
    private readonly LlmOptions _options;
    private readonly ITokenAccountant _accountant;
    private readonly ILogger<ChatClientFactory> _logger;

    public ChatClientFactory(
        IOptions<LlmOptions> options,
        ITokenAccountant accountant,
        ILogger<ChatClientFactory> logger)
    {
        _options = options.Value;
        _accountant = accountant;
        _logger = logger;
    }

    public IChatClient CreateForAgent(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var provider = _options.DefaultProvider;
        var model = _options.DefaultModel;

        if (_options.AgentOverrides.TryGetValue(agentId, out var agentOverride))
        {
            if (!string.IsNullOrWhiteSpace(agentOverride.Provider))
            {
                provider = agentOverride.Provider;
            }

            if (!string.IsNullOrWhiteSpace(agentOverride.Model))
            {
                model = agentOverride.Model;
            }
        }

        return Wrap(BuildInner(provider, model), agentId, provider, model);
    }

    public IChatClient CreateDefault()
        => Wrap(BuildInner(_options.DefaultProvider, _options.DefaultModel), "default", _options.DefaultProvider, _options.DefaultModel);

    private IChatClient BuildInner(string provider, string model)
    {
        _logger.LogDebug("Building IChatClient for provider={Provider} model={Model}", provider, model);

        return provider.ToUpperInvariant() switch
        {
            "AZUREOPENAI" => BuildAzureOpenAi(model),
            "OPENAI" => BuildOpenAi(model),
            "ANTHROPIC" => BuildAnthropic(model),
            "OLLAMA" => BuildOllama(model),
            "MOCK" => new MockChatClient(model),
            _ => BuildFallback(provider, model)
        };
    }

    private IChatClient Wrap(IChatClient inner, string agentId, string provider, string model)
        => new TokenTrackingChatClient(inner, _accountant, agentId, provider, model);

    private IChatClient BuildAzureOpenAi(string model)
    {
        if (!_options.Providers.TryGetValue("AzureOpenAI", out var options) ||
            string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.Endpoint))
        {
            _logger.LogWarning("AzureOpenAI not configured; falling back to MockChatClient");
            return new MockChatClient(model);
        }

        var deployment = options.Deployments.TryGetValue("chat", out var configuredDeployment) &&
                         !string.IsNullOrWhiteSpace(configuredDeployment)
            ? configuredDeployment
            : model;

        var client = new AzureOpenAIClient(new Uri(options.Endpoint, UriKind.Absolute), new AzureKeyCredential(options.ApiKey));
        return client.GetChatClient(deployment).AsIChatClient();
    }

    private IChatClient BuildOpenAi(string model)
    {
        if (!_options.Providers.TryGetValue("OpenAI", out var options) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _logger.LogWarning("OpenAI not configured; falling back to MockChatClient");
            return new MockChatClient(model);
        }

        var client = new OpenAIClient(options.ApiKey);
        return client.GetChatClient(model).AsIChatClient();
    }

    private IChatClient BuildAnthropic(string model)
    {
        if (!_options.Providers.TryGetValue("Anthropic", out var options) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _logger.LogWarning("Anthropic not configured; falling back to MockChatClient");
            return new MockChatClient(model);
        }

        return new AnthropicChatClient(options.ApiKey, model);
    }

    private IChatClient BuildOllama(string model)
    {
        if (!_options.Providers.TryGetValue("Ollama", out var options))
        {
            _logger.LogWarning("Ollama not configured; falling back to MockChatClient");
            return new MockChatClient(model);
        }

        return new OllamaChatClient(options.Endpoint ?? "http://localhost:11434", model);
    }

    private IChatClient BuildFallback(string provider, string model)
    {
        _logger.LogWarning("Unknown LLM provider {Provider}; falling back to MockChatClient", provider);
        return new MockChatClient(model);
    }
}
