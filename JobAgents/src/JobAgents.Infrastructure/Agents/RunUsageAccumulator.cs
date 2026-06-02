using System.Collections.Concurrent;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Per-run token/cost tally. The <see cref="AgentRunner"/> records every agent invocation here so the
/// Coordinator can report a TRUE run total in its terminal System event — specialist agents return
/// domain objects (not usage), so their cost is otherwise invisible to the Coordinator and the System
/// event would under-count to just the Coordinator's own calls.
///
/// Keyed by run because there is no per-run DI scope (Blazor scopes are per-circuit; the runner +
/// coordinator are singletons-per-circuit reused across runs). The Coordinator calls <see cref="Take"/>
/// at the end of a run to read and release its tally.
/// </summary>
public sealed class RunUsageAccumulator
{
    // Safety cap so runs that never call Take (e.g. standalone criteria/JD analysis) can't leak
    // unbounded. Runs are human-paced, so live runs never realistically hit this.
    private const int MaxTrackedRuns = 256;

    private readonly ConcurrentDictionary<RunId, Tally> _byRun = new();

    /// <summary>Records one agent invocation's usage against its run.</summary>
    public void Add(RunId runId, AgentUsage usage)
    {
        if (_byRun.Count >= MaxTrackedRuns && !_byRun.ContainsKey(runId))
            EvictOne();

        var tally = _byRun.GetOrAdd(runId, static _ => new Tally());
        lock (tally)
        {
            tally.TokensIn += usage.TokensIn;
            tally.TokensOut += usage.TokensOut;
            // Cost is unknown if any single call's cost was unknown (unpriced model).
            if (usage.EstimatedCostUsd is { } cost)
                tally.Cost += cost;
            else
                tally.CostKnown = false;
        }
    }

    /// <summary>Reads and removes a run's accumulated usage (<see cref="AgentUsage.Zero"/> if none).</summary>
    public AgentUsage Take(RunId runId)
    {
        if (!_byRun.TryRemove(runId, out var tally))
            return AgentUsage.Zero;

        lock (tally)
            return new AgentUsage(tally.TokensIn, tally.TokensOut, tally.CostKnown ? tally.Cost : null);
    }

    // Best-effort reclamation of a tally whose run ended without calling Take.
    private void EvictOne()
    {
        foreach (var key in _byRun.Keys)
            if (_byRun.TryRemove(key, out _))
                return;
    }

    private sealed class Tally
    {
        public int TokensIn;
        public int TokensOut;
        public decimal Cost;
        public bool CostKnown = true;
    }
}
