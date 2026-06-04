namespace JobAgents.Domain.JobHunt;

/// <summary>
/// One human-labelled real match: the resume + posting + criteria that produced an
/// <see cref="JobMatch"/>, the agent's own score, and the score a human gave it after reviewing.
/// Captured from a live run so it can be replayed as an eval <c>MatchCase</c> — the basis for
/// calibrating the matcher against real judgements instead of only the synthetic golden set.
/// </summary>
public sealed record MatchFeedback(
    string RunId,
    DateTime CreatedAtUtc,
    string Resume,
    JobPosting Posting,
    SearchCriteria Criteria,
    int AgentScore,
    IReadOnlyList<string> AgentMatchedSkills,
    int HumanScore,
    string? Note);
