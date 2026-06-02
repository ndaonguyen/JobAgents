using JobAgents.Application.Abstractions;
using JobAgents.Infrastructure.Agents.Filters;
using JobAgents.Infrastructure.Configuration;
using JobAgents.Infrastructure.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace JobAgents.Infrastructure.Agents;

/// <summary>Builds a fresh <see cref="Kernel"/> per agent role/run so each can use a different model.</summary>
public interface IKernelFactory
{
    Kernel Create(string? modelOverride = null, bool includePlugins = true);

    string DefaultModel { get; }

    /// <summary>True when the (possibly overridden) model resolves to an Anthropic/Claude model.</summary>
    bool IsAnthropicModel(string? modelOverride = null);
}

public sealed class KernelFactory(
    IOptions<JobAgentsOptions> options,
    IHttpClientFactory httpClientFactory,
    EventPublishingFunctionFilter functionFilter,
    AgentRunContext runContext,
    IAgentEventBus bus,
    ILogger<JobSearchPlugin> searchPluginLogger)
    : IKernelFactory
{
    private readonly OpenAiOptions _openAi = options.Value.OpenAi;
    private readonly AnthropicOptions _anthropic = options.Value.Anthropic;

    public string DefaultModel => _openAi.Model;

    public bool IsAnthropicModel(string? modelOverride = null) =>
        IsClaude(modelOverride ?? _openAi.Model);

    private static bool IsClaude(string model) =>
        model.StartsWith("claude", StringComparison.OrdinalIgnoreCase);

    public Kernel Create(string? modelOverride = null, bool includePlugins = true)
    {
        var builder = Kernel.CreateBuilder();

        var model = modelOverride ?? _openAi.Model;

        // Claude is reached via Anthropic's OpenAI-compatible endpoint: same connector, the named
        // "anthropic" HttpClient points at api.anthropic.com/v1 and carries the Anthropic key.
        if (IsClaude(model))
        {
            var anthropicHttp = httpClientFactory.CreateClient("anthropic");
            anthropicHttp.BaseAddress = new Uri(_anthropic.BaseUrl);
            builder.AddOpenAIChatCompletion(
                modelId: model,
                apiKey: _anthropic.ApiKey,
                httpClient: anthropicHttp);
        }
        else
        {
            builder.AddOpenAIChatCompletion(
                modelId: model,
                apiKey: _openAi.ApiKey,
                httpClient: httpClientFactory.CreateClient("openai"));
        }

        var kernel = builder.Build();

        if (includePlugins)
        {
            var searchHttp = httpClientFactory.CreateClient("tavily");
            var searchPlugin = new JobSearchPlugin(searchHttp, options, runContext, bus, searchPluginLogger);
            kernel.Plugins.AddFromObject(searchPlugin, "Web");
        }

        kernel.FunctionInvocationFilters.Add(functionFilter);
        return kernel;
    }
}
