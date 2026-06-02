using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

public interface IResumeMatchAgent
{
    Task<JobMatch> MatchAsync(
        RunId runId, int index, string resumeText, JobPosting posting, SearchCriteria criteria,
        JobHuntConfig config, CancellationToken ct);
}

/// <summary>Scores a single posting against the candidate's resume and explains the fit.</summary>
public sealed class ResumeMatchAgent(IAgentRunner runner) : IResumeMatchAgent
{
    private const string SystemPrompt =
        """
        You are a resume-matching agent. Given a candidate's resume and ONE job posting, assess fit
        honestly. Return ONLY a JSON object:
        {
          "score": integer 0-100,
          "matchedSkills": string[],
          "gaps": string[],
          "rationale": string
        }
        Scoring guide: 85-100 strong fit, 70-84 good, 60-69 borderline, below 60 weak.
        Weigh the candidate's TARGET CRITERIA heavily: a posting that misses the must-have skills,
        target roles or seniority should score low even if it is broadly in the same field.
        - "matchedSkills": concrete skills/experience from the resume that the posting asks for.
        - "gaps": specific requirements the candidate appears to lack or under-demonstrate.
        - "rationale": 2-4 sentences citing SPECIFIC evidence — name the candidate's relevant
          experience and the posting's key requirements, and explain seniority/domain fit. Avoid
          generic filler; reference real details from both texts.
        Score against the JOB DESCRIPTION (the detailed requirements), not just the short summary.
        Base everything only on the resume, posting and criteria provided; do not invent experience.
        """;

    private sealed record MatchDto(int Score, List<string>? MatchedSkills, List<string>? Gaps, string? Rationale);

    public async Task<JobMatch> MatchAsync(
        RunId runId, int index, string resumeText, JobPosting posting, SearchCriteria criteria,
        JobHuntConfig config, CancellationToken ct)
    {
        var description = string.IsNullOrWhiteSpace(posting.Description) ? posting.Summary : posting.Description;
        var userPrompt =
            $"""
            CANDIDATE RESUME:
            {resumeText}

            TARGET CRITERIA:
            - Target roles: {Join(criteria.Roles)}
            - Seniority: {Or(criteria.Seniority)}
            - Must-have skills: {Join(criteria.MustHaveSkills)}
            - Nice-to-have skills: {Join(criteria.NiceToHaveSkills)}
            - Remote only: {criteria.RemoteOnly}

            JOB POSTING:
            Title: {posting.Title}
            Company: {posting.Company}
            Location: {posting.Location}
            Summary: {posting.Summary}
            Description: {description}
            URL: {posting.Url}
            """;

        var result = await runner.RunAsync(
            runId, AgentId.ResumeMatch(index), "Resume Matching",
            SystemPrompt, userPrompt, config.ResumeMatchModel, useTools: false, ct, jsonMode: true);

        var dto = AgentJson.TryParse<MatchDto>(result.Text);
        return new JobMatch(
            Posting: posting,
            Score: Math.Clamp(dto?.Score ?? 0, 0, 100),
            MatchedSkills: dto?.MatchedSkills ?? [],
            Gaps: dto?.Gaps ?? [],
            Rationale: dto?.Rationale ?? "(no rationale produced)");
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(any)" : string.Join(", ", values);

    private static string Or(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(any)" : value;
}
