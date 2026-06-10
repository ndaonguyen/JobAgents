namespace JobAgents.Web.Services;

/// <summary>
/// Rough per-run cost estimates for the "Maximum results" picker. Cost scales mainly with the number of
/// postings matched (one resume-matcher LLM call each), so we model it as a per-result rate × the cap.
/// The rate is learned from past runs when available (most honest), with a static fallback otherwise.
/// Estimates only — the real cost depends on resume/description length, models, and search depth.
/// </summary>
public static class RunCostEstimator
{
    /// <summary>The result caps offered in Settings.</summary>
    public static readonly IReadOnlyList<int> MaxResultsOptions = [10, 12, 15, 18];

    /// <summary>
    /// Fallback marginal $/result when there's no run history to learn from. Dominated by the resume
    /// matcher: ~one Claude Haiku call per posting (≈4k in + ≈0.6k out at $1/$5 per 1M ≈ $0.007), plus a
    /// share of search/sourcing. Deliberately rough and labelled as such in the UI.
    /// </summary>
    public const decimal FallbackPerResult = 0.012m;

    /// <summary>
    /// Mean USD cost per returned result across past runs that reported a cost and produced ≥1 result.
    /// Null when there's nothing to learn from (callers fall back to <see cref="FallbackPerResult"/>).
    /// </summary>
    public static decimal? PerResultFromHistory(IEnumerable<PersistedRun> runs)
    {
        var samples = runs
            .Where(r => r.EstimatedCostUsd is > 0m && r.Result.Dossiers.Count > 0)
            .Select(r => r.EstimatedCostUsd!.Value / r.Result.Dossiers.Count)
            .ToList();

        return samples.Count == 0 ? null : samples.Average();
    }

    /// <summary>Estimated $/run for a result cap, and whether the rate came from real run history.</summary>
    public static (decimal Cost, bool FromHistory) Estimate(int maxResults, decimal? perResultFromHistory) =>
        ((perResultFromHistory ?? FallbackPerResult) * maxResults, perResultFromHistory is not null);
}
