using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Domain.Events;

/// <summary>
/// Base type for everything that streams out of a run. <see cref="Kind"/> is a stable string
/// discriminator the UI uses to route each event without reflecting over the .NET type.
/// </summary>
public abstract record AgentEvent(RunId RunId, AgentId AgentId, DateTime Timestamp)
{
    public abstract string Kind { get; }
}

/// <summary>An agent has begun working.</summary>
public sealed record AgentStartedEvent(RunId RunId, AgentId AgentId, string Role, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "agent.started";
}

/// <summary>A streamed token/delta from an agent's response.</summary>
public sealed record AgentTokenEvent(RunId RunId, AgentId AgentId, string Delta, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "agent.token";
}

/// <summary>
/// An agent has finished. When <see cref="AgentEvent.AgentId"/> equals <see cref="AgentId.System"/>
/// this is the terminal event for the whole run and carries aggregated usage.
/// </summary>
public sealed record AgentFinishedEvent(
    RunId RunId,
    AgentId AgentId,
    string FinalText,
    int TokensIn,
    int TokensOut,
    decimal? EstimatedCostUsd,
    DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "agent.finished";
}

/// <summary>An agent failed. A System-level error terminates the run.</summary>
public sealed record AgentErrorEvent(RunId RunId, AgentId AgentId, string Message, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "agent.error";
}

/// <summary>A tool/function call was invoked (captured by the kernel function filter).</summary>
public sealed record ToolCalledEvent(
    RunId RunId, AgentId AgentId, string ToolName, string ArgumentsJson, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "tool.called";
}

/// <summary>A tool/function call returned.</summary>
public sealed record ToolResultEvent(
    RunId RunId, AgentId AgentId, string ToolName, string ResultJson, long DurationMs, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "tool.result";
}

/// <summary>
/// One actual web-search (Tavily) HTTP request was issued. Distinct from <see cref="ToolCalledEvent"/>:
/// a single search_web tool call can issue two requests (a domain-restricted attempt plus a whole-web
/// fallback), so counting these reflects true external request volume.
/// </summary>
public sealed record WebSearchRequestedEvent(
    RunId RunId, AgentId AgentId, string Query, bool IsFallback, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "websearch.requested";
}

/// <summary>The Search agent produced a candidate list of postings.</summary>
public sealed record JobsFoundEvent(
    RunId RunId, AgentId AgentId, IReadOnlyList<JobPosting> Postings, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "jobs.found";
}

/// <summary>A Resume-Matching agent scored one posting.</summary>
public sealed record JobMatchedEvent(RunId RunId, AgentId AgentId, JobMatch Match, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "job.matched";
}

/// <summary>A Company-Research agent produced an insight.</summary>
public sealed record CompanyResearchedEvent(
    RunId RunId, AgentId AgentId, CompanyInsight Insight, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "company.researched";
}

/// <summary>A Salary-Analysis agent produced an estimate.</summary>
public sealed record SalaryAnalyzedEvent(
    RunId RunId, AgentId AgentId, SalaryEstimate Estimate, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "salary.analyzed";
}

/// <summary>An Interview-Preparation agent produced prep material.</summary>
public sealed record InterviewPrepReadyEvent(
    RunId RunId, AgentId AgentId, InterviewPrep Prep, DateTime Timestamp)
    : AgentEvent(RunId, AgentId, Timestamp)
{
    public override string Kind => "interview.prep";
}
