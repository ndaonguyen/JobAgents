namespace JobAgents.Domain.Analysis;

/// <summary>
/// One gap between the candidate's resume and a job description, with a severity and concrete advice
/// on how to close (or honestly present around) it.
/// </summary>
public sealed record JdGap(
    string Requirement,
    string Severity,
    string Advice);

/// <summary>
/// The result of analysing a candidate's resume against a single pasted job description: an overall
/// fit score, the strengths that match, the gaps, and actionable advice for closing them and
/// tailoring the application.
/// </summary>
public sealed record JdAnalysis(
    int OverallScore,
    string Verdict,
    IReadOnlyList<string> MatchedStrengths,
    IReadOnlyList<JdGap> Gaps,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> CvSuggestions,
    IReadOnlyList<string> InterviewTalkingPoints,
    string Summary)
{
    public static JdAnalysis Empty { get; } = new(
        0, "(no analysis produced)", [], [], [], [], [], string.Empty);
}
