using JobAgents.Application.JobHunt;
using Microsoft.Extensions.DependencyInjection;

namespace JobAgents.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registers application-layer use cases. Ports are wired in the Infrastructure layer.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<StartJobHuntUseCase>();
        return services;
    }
}
