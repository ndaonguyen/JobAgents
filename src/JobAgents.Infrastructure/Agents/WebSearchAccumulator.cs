using System.Collections.Concurrent;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Per-run tally of REAL Tavily requests (cache misses only), attributed to the agent that issued
/// them. The <see cref="Plugins.JobSearchPlugin"/> records each outbound request here, and the
/// Coordinator reads the breakdown at the end of a run to log where the run's web-search budget went
/// (search vs. company-research vs. salary-analysis), so the true split is visible instead of having
/// to reason about per-agent ceilings.
///
/// Mirrors <see cref="RunUsageAccumulator"/>: keyed by run because there is no per-run DI scope, and
/// the Coordinator calls <see cref="Take"/> once to read and release a run's tally.
/// </summary>
public sealed class WebSearchAccumulator
{
    // Safety cap so runs that never call Take can't leak unbounded (see RunUsageAccumulator).
    private const int MaxTrackedRuns = 256;

    private readonly ConcurrentDictionary<RunId, Tally> _byRun = new();

    /// <summary>Records one real (uncached) Tavily request against its run and issuing agent.</summary>
    public void Add(RunId runId, AgentId agent, bool isFallback)
    {
        if (_byRun.Count >= MaxTrackedRuns && !_byRun.ContainsKey(runId))
            EvictOne();

        var tally = _byRun.GetOrAdd(runId, static _ => new Tally());
        lock (tally)
        {
            var kind = Kind(agent);
            tally.ByAgent[kind] = tally.ByAgent.GetValueOrDefault(kind) + 1;
            tally.Total++;
            if (isFallback)
                tally.Fallbacks++;
        }
    }

    /// <summary>Reads and removes a run's tally as a one-line summary ("(none)" if nothing searched).</summary>
    public string TakeSummary(RunId runId)
    {
        if (!_byRun.TryRemove(runId, out var tally))
            return "0 Tavily request(s)";

        lock (tally)
        {
            if (tally.Total == 0)
                return "0 Tavily request(s)";

            var perAgent = string.Join(", ",
                tally.ByAgent.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}: {kv.Value}"));
            var fallback = tally.Fallbacks > 0 ? $" ({tally.Fallbacks} fallback)" : string.Empty;
            return $"{tally.Total} Tavily request(s) — {perAgent}{fallback}";
        }
    }

    // "salary-analysis-2" -> "salary-analysis"; "search" -> "search". Collapses the per-posting index
    // so fan-out agents are summed under one kind.
    private static string Kind(AgentId agent)
    {
        var value = agent.Value;
        var dash = value.LastIndexOf('-');
        return dash > 0 && int.TryParse(value.AsSpan(dash + 1), out _) ? value[..dash] : value;
    }

    // Best-effort reclamation of a tally whose run ended without calling TakeSummary.
    private void EvictOne()
    {
        foreach (var key in _byRun.Keys)
            if (_byRun.TryRemove(key, out _))
                return;
    }

    private sealed class Tally
    {
        public readonly Dictionary<string, int> ByAgent = new(StringComparer.Ordinal);
        public int Total;
        public int Fallbacks;
    }
}
