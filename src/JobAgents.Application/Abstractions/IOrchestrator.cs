using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Application.Abstractions;

/// <summary>
/// The Coordinator. Owns the end-to-end job-hunt pipeline — parse criteria → search → match →
/// rank → expand (company / salary / interview prep) → synthesise — publishing every step to the
/// <see cref="IAgentEventBus"/> and owning the terminal System-level finished event.
/// </summary>
public interface IOrchestrator
{
    Task RunAsync(AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default);

    /// <summary>
    /// Parses the resume + preferences into structured criteria without running a search. Lets the UI
    /// show the inferred must-have / nice-to-have skills and work modes for the user to edit before
    /// kicking off the full run.
    /// </summary>
    Task<SearchCriteria> ParseCriteriaAsync(AgentRunRequest request, JobHuntConfig config, CancellationToken ct = default);
}
