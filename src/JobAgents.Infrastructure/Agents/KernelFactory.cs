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
    ILogger<JobSearchPlugin> searchPluginLogger,
    TavilySearchCache searchCache,
    WebSearchAccumulator searchCounts)
    : IKernelFactory
{
    private readonly OpenAiOptions _openAi = options.Value.OpenAi;
    private readonly AnthropicOptions _anthropic = options.Value.Anthropic;

    public string DefaultModel => _openAi.Model;

    public bool IsAnthropicModel(string? modelOverride = null) =>
        IsClaude(modelOverride ?? _openAi.Model);

    private static bool IsClaude(string model) =>
        model.StartsWith("claude", StringComparison.OrdinalIgnoreCase);

    // Anthropic's API rejects bare base ids (HTTP 404 not_found_error) — it wants a dated snapshot or a
    // documented alias. The model picker stores short ids (and old configs already saved them), so map
    // them to valid API ids here, at the one place a model id is handed to the connector. Pricing stays
    // prefix-based, so the resolved id still matches its row.
    private static readonly Dictionary<string, string> ClaudeIdAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5"] = "claude-haiku-4-5-20251001",
        ["claude-sonnet-4"]  = "claude-sonnet-4-0",
        ["claude-opus-4"]    = "claude-opus-4-0",
    };

    private static string ResolveModelId(string model) =>
        ClaudeIdAliases.TryGetValue(model, out var resolved) ? resolved : model;

    public Kernel Create(string? modelOverride = null, bool includePlugins = true)
    {
        var builder = Kernel.CreateBuilder();

        var model = ResolveModelId(modelOverride ?? _openAi.Model);

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
            var searchPlugin = new JobSearchPlugin(searchHttp, options, runContext, bus, searchPluginLogger, searchCache, searchCounts);
            kernel.Plugins.AddFromObject(searchPlugin, "Web");
        }

        kernel.FunctionInvocationFilters.Add(functionFilter);
        return kernel;
    }
}
