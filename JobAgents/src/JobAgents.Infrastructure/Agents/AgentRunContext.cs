using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Flows the currently-executing (run, agent) pair down the async call chain via
/// <see cref="AsyncLocal{T}"/>, so the kernel function filter can attribute tool calls to the right
/// run and agent without threading parameters through every plugin. Because each fanned-out agent
/// runs on its own async context, concurrent agents see their own values.
/// </summary>
public sealed class AgentRunContext
{
    private static readonly AsyncLocal<RunId?> _runId = new();
    private static readonly AsyncLocal<AgentId?> _agentId = new();
    private static readonly AsyncLocal<IReadOnlyList<string>?> _includeDomains = new();
    private static readonly AsyncLocal<string?> _timeRange = new();
    private static readonly AsyncLocal<string?> _startDate = new();
    private static readonly AsyncLocal<string?> _endDate = new();
    private static readonly AsyncLocal<SearchDepthSettings?> _searchDepth = new();

    public RunId? CurrentRun
    {
        get => _runId.Value;
        set => _runId.Value = value;
    }

    public AgentId? CurrentAgent
    {
        get => _agentId.Value;
        set => _agentId.Value = value;
    }

    /// <summary>Domains to restrict web search to for this run (empty = search the whole web).</summary>
    public IReadOnlyList<string> IncludeDomains
    {
        get => _includeDomains.Value ?? Array.Empty<string>();
        set => _includeDomains.Value = value;
    }

    /// <summary>Recency window for job-sourcing web search (Tavily time_range): day/week/month/year, or null.</summary>
    public string? TimeRange
    {
        get => _timeRange.Value;
        set => _timeRange.Value = value;
    }

    /// <summary>Exact job-sourcing date bounds (YYYY-MM-DD). When set, they take precedence over TimeRange.</summary>
    public string? StartDate
    {
        get => _startDate.Value;
        set => _startDate.Value = value;
    }

    public string? EndDate
    {
        get => _endDate.Value;
        set => _endDate.Value = value;
    }

    /// <summary>Per-call Tavily search-depth policy for this run (falls back to the defaults when unset).</summary>
    public SearchDepthSettings SearchDepth
    {
        get => _searchDepth.Value ?? SearchDepthSettings.Default;
        set => _searchDepth.Value = value;
    }

    public void Set(RunId runId, AgentId agentId)
    {
        _runId.Value = runId;
        _agentId.Value = agentId;
    }
}
