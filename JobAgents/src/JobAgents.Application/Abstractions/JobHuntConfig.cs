namespace JobAgents.Application.Abstractions;

/// <summary>The kind of web-search call, so its Tavily depth can be chosen independently.</summary>
public enum SearchCallKind
{
    /// <summary>Job sourcing — the Search agent's primary query (domain-restricted when sites are picked).</summary>
    Sourcing,
    /// <summary>Job sourcing whole-web fallback when the domain-restricted query found nothing.</summary>
    SourcingFallback,
    /// <summary>Company-research lookup.</summary>
    CompanyResearch,
    /// <summary>Salary-analysis lookup.</summary>
    SalaryAnalysis,
    /// <summary>Anything else (defaults to basic).</summary>
    Other,
}

/// <summary>
/// Per-call Tavily search-depth policy. "advanced" costs 2 API credits and gives much better recall
/// on niche / JS-heavy job boards; "basic" costs 1 and is enough for the open web. Each flag is
/// <c>true</c> for advanced, <c>false</c> for basic. Defaults: advanced only for primary job sourcing.
/// </summary>
public sealed record SearchDepthSettings(
    bool Sourcing = true,
    bool SourcingFallback = false,
    bool CompanyResearch = false,
    bool SalaryAnalysis = false)
{
    public static SearchDepthSettings Default { get; } = new();

    /// <summary>True when the given call kind should use Tavily "advanced" depth.</summary>
    public bool IsAdvanced(SearchCallKind kind) => kind switch
    {
        SearchCallKind.Sourcing => Sourcing,
        SearchCallKind.SourcingFallback => SourcingFallback,
        SearchCallKind.CompanyResearch => CompanyResearch,
        SearchCallKind.SalaryAnalysis => SalaryAnalysis,
        _ => false,
    };
}

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
    bool ResearchCompany = true,
    bool ResearchSalary = true,
    SearchDepthSettings? SearchDepth = null,
    IReadOnlyList<string>? IncludeDomains = null,
    string? TimeRange = null,
    string? StartDate = null,
    string? EndDate = null)
{
    public static JobHuntConfig Default { get; } = new();
}
