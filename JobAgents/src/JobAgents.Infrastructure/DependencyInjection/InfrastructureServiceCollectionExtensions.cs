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
        services.AddHttpClient("tavily");

        // Run infrastructure (shared, stateless).
        services.AddSingleton<IAgentEventBus, ChannelAgentEventBus>();
        services.AddSingleton<AgentRunContext>();
        services.AddSingleton<EventPublishingFunctionFilter>();
        services.AddSingleton<IKernelFactory, KernelFactory>();
        services.AddSingleton<IUsageCalculator, ModelPricingCalculator>();
        services.AddSingleton<IWorkingMemory, NullWorkingMemory>();

        // Agents + coordinator.
        services.AddScoped<IAgentRunner, AgentRunner>();
        services.AddScoped<ISearchAgent, SearchAgent>();
        services.AddScoped<IResumeMatchAgent, ResumeMatchAgent>();
        services.AddScoped<ICompanyResearchAgent, CompanyResearchAgent>();
        services.AddScoped<ISalaryAnalysisAgent, SalaryAnalysisAgent>();
        services.AddScoped<IInterviewPrepAgent, InterviewPrepAgent>();
        services.AddScoped<IOrchestrator, Coordinator>();

        return services;
    }
}
