using JobAgents.Domain.JobHunt;

namespace JobAgents.Application.Abstractions;

/// <summary>
/// Researches a single match on demand — running the company / salary / interview specialists for
/// one posting and returning the full dossier. Used when the user expands a match-only card.
/// </summary>
public interface IMatchExpander
{
    Task<JobDossier> ExpandAsync(
        JobMatch match, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct = default);

    /// <summary>Researches just the hiring company for one match (one agent, its own Tavily calls).</summary>
    Task<CompanyInsight> ResearchCompanyAsync(
        JobMatch match, JobHuntConfig config, CancellationToken ct = default);

    /// <summary>Estimates just the salary range for one match.</summary>
    Task<SalaryEstimate> ResearchSalaryAsync(
        JobMatch match, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct = default);

    /// <summary>Prepares just the interview guidance for one match.</summary>
    Task<InterviewPrep> ResearchInterviewAsync(
        JobMatch match, JobHuntConfig config, CancellationToken ct = default);
}
