using JobAgents.Infrastructure.Agents.Filters;
using JobAgents.Infrastructure.Configuration;
using JobAgents.Infrastructure.Plugins;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace JobAgents.Infrastructure.Agents;

/// <summary>Builds a fresh <see cref="Kernel"/> per agent role/run so each can use a different model.</summary>
public interface IKernelFactory
{
    Kernel Create(string? modelOverride = null, bool includePlugins = true);

    string DefaultModel { get; }
}

public sealed class KernelFactory(
    IOptions<JobAgentsOptions> options,
    IHttpClientFactory httpClientFactory,
    EventPublishingFunctionFilter functionFilter,
    AgentRunContext runContext)
    : IKernelFactory
{
    private readonly OpenAiOptions _openAi = options.Value.OpenAi;

    public string DefaultModel => _openAi.Model;

    public Kernel Create(string? modelOverride = null, bool includePlugins = true)
    {
        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: modelOverride ?? _openAi.Model,
            apiKey: _openAi.ApiKey,
            httpClient: httpClientFactory.CreateClient("openai"));

        var kernel = builder.Build();

        if (includePlugins)
        {
            var searchHttp = httpClientFactory.CreateClient("tavily");
            var searchPlugin = new JobSearchPlugin(searchHttp, options, runContext);
            kernel.Plugins.AddFromObject(searchPlugin, "Web");
        }

        kernel.FunctionInvocationFilters.Add(functionFilter);
        return kernel;
    }
}
