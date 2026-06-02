namespace JobAgents.Application.Abstractions;

/// <summary>
/// Per-run knobs for the Coordinator. Per-role model overrides let each agent run on a different
/// model; the fan-out limits bound cost and latency.
/// </summary>
public sealed record JobHuntConfig(
    string? CoordinatorModel = null,
    string? SearchModel = null,
    string? ResumeMatchModel = null,
    string? CompanyResearchModel = null,
    string? SalaryAnalysisModel = null,
    string? InterviewPrepModel = null,
    int MaxResults = 12,
    int MaxSearches = 6,
    int MaxFanOutConcurrency = 3,
    int TopMatchesToExpand = 3,
    int MinMatchScore = 60,
    IReadOnlyList<string>? IncludeDomains = null,
    string? TimeRange = null,
    string? StartDate = null,
    string? EndDate = null)
{
    public static JobHuntConfig Default { get; } = new();
}
