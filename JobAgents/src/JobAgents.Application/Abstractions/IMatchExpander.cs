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
}
