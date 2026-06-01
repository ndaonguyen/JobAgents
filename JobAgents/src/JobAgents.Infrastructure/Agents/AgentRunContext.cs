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

    public void Set(RunId runId, AgentId agentId)
    {
        _runId.Value = runId;
        _agentId.Value = agentId;
    }
}
