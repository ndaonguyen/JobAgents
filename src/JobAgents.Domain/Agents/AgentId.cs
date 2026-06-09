namespace JobAgents.Domain.Agents;

/// <summary>
/// Strongly-typed identifier for an agent participating in a run. The Coordinator owns the
/// terminal events under <see cref="System"/>; specialist agents that fan out per job carry
/// an index so the UI can attribute their events to a specific posting.
/// </summary>
public readonly record struct AgentId(string Value)
{
    /// <summary>The orchestrator / run-level agent. Owns terminal finished/error events.</summary>
    public static AgentId System { get; } = new("system");

    /// <summary>The Coordinator agent that parses criteria and synthesises the final result.</summary>
    public static AgentId Coordinator { get; } = new("coordinator");

    /// <summary>The Search agent that sources live job postings.</summary>
    public static AgentId Search { get; } = new("search");

    /// <summary>The standalone JD gap-analysis agent (resume vs a pasted job description).</summary>
    public static AgentId JdAnalysis { get; } = new("jd-analysis");

    public static AgentId ResumeMatch(int index) => new($"resume-match-{index}");

    public static AgentId CompanyResearch(int index) => new($"company-research-{index}");

    public static AgentId SalaryAnalysis(int index) => new($"salary-analysis-{index}");

    public static AgentId InterviewPrep(int index) => new($"interview-prep-{index}");

    public override string ToString() => Value;
}
