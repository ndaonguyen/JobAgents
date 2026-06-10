using JobAgents.Application.Abstractions;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.Agents.Filters;
using JobAgents.Infrastructure.Configuration;
using JobAgents.Infrastructure.EventBus;
using JobAgents.Infrastructure.Memory;
using JobAgents.Infrastructure.Pricing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobAgents.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<JobAgentsOptions>()
            .Bind(configuration.GetSection(JobAgentsOptions.SectionName));

        services.AddHttpClient("openai");
        services.AddHttpClient("anthropic");
        services.AddHttpClient("tavily");

        // Run infrastructure (shared, stateless).
        services.AddSingleton<IAgentEventBus, ChannelAgentEventBus>();
        services.AddSingleton<AgentRunContext>();
        services.AddSingleton<RunUsageAccumulator>();
        services.AddSingleton<WebSearchAccumulator>();
        services.AddSingleton<Plugins.TavilySearchCache>();
        services.AddSingleton<EventPublishingFunctionFilter>();
        services.AddSingleton<IKernelFactory, KernelFactory>();
        services.AddSingleton<IUsageCalculator, ModelPricingCalculator>();
        services.AddSingleton<IWorkingMemory, NullWorkingMemory>();
        // Semantic-retrieval embeddings (disabled at runtime when no OpenAI key is set).
        services.AddSingleton<IEmbeddingService, Sourcing.OpenAiEmbeddingService>();
        // Default to a no-op posting store; the web app overrides this with a file-backed one
        // pointed at its results directory.
        services.AddSingleton<Sourcing.IPostingStore, Sourcing.NullPostingStore>();

        // Agents + coordinator.
        services.AddScoped<IAgentRunner, AgentRunner>();
        services.AddScoped<ISearchAgent, SearchAgent>();
        services.AddScoped<IResumeMatchAgent, ResumeMatchAgent>();
        services.AddScoped<ICompanyResearchAgent, CompanyResearchAgent>();
        services.AddScoped<ISalaryAnalysisAgent, SalaryAnalysisAgent>();
        services.AddScoped<IInterviewPrepAgent, InterviewPrepAgent>();
        services.AddScoped<IOrchestrator, Coordinator>();
        services.AddScoped<IMatchExpander, MatchExpander>();

        // Standalone JD gap-analysis (separate from the job-hunt pipeline).
        services.AddScoped<IJdAnalysisAgent, JdAnalysisAgent>();

        return services;
    }
}
