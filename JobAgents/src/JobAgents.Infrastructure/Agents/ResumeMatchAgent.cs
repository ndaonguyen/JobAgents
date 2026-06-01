using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

public interface IResumeMatchAgent
{
    Task<JobMatch> MatchAsync(
        RunId runId, int index, string resumeText, JobPosting posting, JobHuntConfig config, CancellationToken ct);
}

/// <summary>Scores a single posting against the candidate's resume and explains the fit.</summary>
public sealed class ResumeMatchAgent(IAgentRunner runner) : IResumeMatchAgent
{
    private const string SystemPrompt =
        """
        You are a resume-matching agent. Given a candidate's resume and ONE job posting, assess fit.
        Return ONLY a JSON object:
        {
          "score": integer 0-100,
          "matchedSkills": string[],
          "gaps": string[],
          "rationale": string
        }
        "score" reflects how well the candidate fits this role. "gaps" are requirements the candidate
        appears to lack. Be honest and specific; base everything on the resume and posting provided.
        """;

    private sealed record MatchDto(int Score, List<string>? MatchedSkills, List<string>? Gaps, string? Rationale);

    public async Task<JobMatch> MatchAsync(
        RunId runId, int index, string resumeText, JobPosting posting, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt =
            $"""
            CANDIDATE RESUME:
            {resumeText}

            JOB POSTING:
            Title: {posting.Title}
            Company: {posting.Company}
            Location: {posting.Location}
            Summary: {posting.Summary}
            URL: {posting.Url}
            """;

        var result = await runner.RunAsync(
            runId, AgentId.ResumeMatch(index), "Resume Matching",
            SystemPrompt, userPrompt, config.ResumeMatchModel, useTools: false, ct);

        var dto = AgentJson.TryParse<MatchDto>(result.Text);
        return new JobMatch(
            Posting: posting,
            Score: Math.Clamp(dto?.Score ?? 0, 0, 100),
            MatchedSkills: dto?.MatchedSkills ?? [],
            Gaps: dto?.Gaps ?? [],
            Rationale: dto?.Rationale ?? "(no rationale produced)");
    }
}
