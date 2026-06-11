namespace JobAgents.Web.Services;

/// <summary>
/// Rough per-run cost estimates for the "Maximum results" picker, modelled as <c>base + perResult × N</c>:
/// a fixed overhead (coordinator parse/synthesis + the top-3 dossier expansions, which don't scale with
/// the cap) plus a marginal cost per matched posting (one resume-matcher call each). Both terms are
/// learned from past runs via a least-squares fit when there's enough history, with static fallbacks
/// otherwise. Estimates only — real cost depends on resume/description length, models, and search depth.
/// </summary>
public static class RunCostEstimator
{
    /// <summary>The result caps offered in Settings.</summary>
    public static readonly IReadOnlyList<int> MaxResultsOptions = [10, 12, 15, 18];

    /// <summary>
    /// Fallback fixed overhead per run when there's no history to learn from: coordinator parse +
    /// synthesis and the top-3 expansions (company/salary/interview), roughly independent of the cap.
    /// </summary>
    public const decimal FallbackBase = 0.06m;

    /// <summary>
    /// Fallback marginal $/result: ~one Claude Haiku matcher call per posting (≈4k in + ≈0.6k out at
    /// $1/$5 per 1M ≈ $0.007). Deliberately rough and labelled as such in the UI.
    /// </summary>
    public const decimal FallbackPerResult = 0.008m;

    /// <summary>A cost model: fixed <paramref name="Base"/> plus <paramref name="PerResult"/> per posting.</summary>
    public sealed record CostModel(decimal Base, decimal PerResult, bool FromHistory)
    {
        public decimal Estimate(int maxResults) => Base + PerResult * maxResults;
    }

    /// <summary>
    /// Fits <c>cost ≈ base + perResult × results</c> over past runs by ordinary least squares. Needs ≥2
    /// distinct result counts to separate the fixed and marginal terms; otherwise (or if the fit is
    /// degenerate/negative) returns the static fallback model.
    /// </summary>
    public static CostModel Fit(IEnumerable<PersistedRun> runs)
    {
        var pts = runs
            .Where(r => r.EstimatedCostUsd is > 0m && r.Result.Dossiers.Count > 0)
            .Select(r => (X: (double)r.Result.Dossiers.Count, Y: (double)r.EstimatedCostUsd!.Value))
            .ToList();

        if (pts.Select(p => p.X).Distinct().Count() >= 2)
        {
            var n = pts.Count;
            double sx = pts.Sum(p => p.X), sy = pts.Sum(p => p.Y);
            double sxx = pts.Sum(p => p.X * p.X), sxy = pts.Sum(p => p.X * p.Y);
            var denom = n * sxx - sx * sx;
            if (denom != 0)
            {
                var slope = (n * sxy - sx * sy) / denom;
                var intercept = (sy - slope * sx) / n;
                // Only trust a fit that's physically sensible (positive marginal, non-negative base).
                if (slope > 0 && intercept >= 0)
                    return new CostModel((decimal)intercept, (decimal)slope, true);
            }
        }

        return new CostModel(FallbackBase, FallbackPerResult, false);
    }
}
