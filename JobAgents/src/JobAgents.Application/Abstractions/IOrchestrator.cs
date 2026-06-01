using JobAgents.Domain.Agents;

namespace JobAgents.Application.Abstractions;

/// <summary>
/// The Coordinator. Owns the end-to-end job-hunt pipeline — parse criteria → search → match →
/// rank → expand (company / salary / interview prep) → synthesise — publishing every step to the
/// <see cref="IAgentEventBus"/> and owning the terminal System-level finished event.
/// </summary>
public interface IOrchestrator
{
    Task RunAsync(AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default);
}
