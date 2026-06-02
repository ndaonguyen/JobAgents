namespace JobAgents.Domain.JobHunt;

/// <summary>
/// Structured search intent parsed by the Coordinator from the candidate's resume + preferences.
/// </summary>
public sealed record SearchCriteria(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Locations,
    string Seniority,
    IReadOnlyList<string> MustHaveSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    bool RemoteOnly,
    string? SalaryExpectation)
{
    public static SearchCriteria Empty { get; } = new(
        Array.Empty<string>(), Array.Empty<string>(), string.Empty,
        Array.Empty<string>(), Array.Empty<string>(), false, null);
}

/// <summary>A single job posting sourced by the Search agent.</summary>
public sealed record JobPosting(
    string Title,
    string Company,
    string Location,
    string Url,
    string Summary,
    string? PostedDate = null,
    string? Description = null);

/// <summary>The Resume-Matching agent's assessment of one posting against the candidate.</summary>
public sealed record JobMatch(
    JobPosting Posting,
    int Score,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> Gaps,
    string Rationale);

/// <summary>The Company-Research agent's insight about a hiring company.</summary>
public sealed record CompanyInsight(
    string Company,
    string Summary,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> RecentNews);

/// <summary>The Salary-Analysis agent's estimate for a role + location + seniority.</summary>
public sealed record SalaryEstimate(
    decimal? Low,
    decimal? Median,
    decimal? High,
    string Currency,
    string Basis);

/// <summary>The Interview-Preparation agent's tailored prep for a posting.</summary>
public sealed record InterviewPrep(
    IReadOnlyList<string> LikelyQuestions,
    IReadOnlyList<string> PrepNotes);

/// <summary>
/// The fully expanded view of one top match: the posting + every specialist's contribution.
/// Specialist fields are nullable because expansion is best-effort (an agent may fail).
/// </summary>
public sealed record JobDossier(
    JobMatch Match,
    CompanyInsight? Company,
    SalaryEstimate? Salary,
    InterviewPrep? Interview);

/// <summary>The terminal result of a run: ranked dossiers plus a short Coordinator summary.</summary>
public sealed record JobHuntResult(
    SearchCriteria Criteria,
    IReadOnlyList<JobDossier> Dossiers,
    string Summary);
