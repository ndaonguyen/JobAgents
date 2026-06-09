using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

public interface IInterviewPrepAgent
{
    Task<InterviewPrep> PrepareAsync(
        RunId runId, int index, JobPosting posting, JobMatch match, JobHuntConfig config, CancellationToken ct);
}

/// <summary>Produces likely interview questions and prep notes tailored to the role and the candidate's gaps.</summary>
public sealed class InterviewPrepAgent(IAgentRunner runner) : IInterviewPrepAgent
{
    private const string SystemPrompt =
        """
        You are an interview-preparation agent. Given a job posting and the candidate's fit
        assessment (matched skills and gaps), produce focused prep. Return ONLY a JSON object:
        {
          "likelyQuestions": string[],
          "prepNotes": string[]
        }
        "likelyQuestions" are realistic interview questions for this role. "prepNotes" are concrete
        tips, prioritising the candidate's gaps. Keep each item concise and actionable.
        """;

    private sealed record PrepDto(List<string>? LikelyQuestions, List<string>? PrepNotes);

    public async Task<InterviewPrep> PrepareAsync(
        RunId runId, int index, JobPosting posting, JobMatch match, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt =
            $"""
            JOB POSTING:
            Title: {posting.Title}
            Company: {posting.Company}
            Summary: {posting.Summary}

            CANDIDATE FIT:
            Matched skills: {string.Join(", ", match.MatchedSkills)}
            Gaps: {string.Join(", ", match.Gaps)}
            """;

        var result = await runner.RunAsync(
            runId, AgentId.InterviewPrep(index), "Interview Preparation",
            SystemPrompt, userPrompt, config.InterviewPrepModel, useTools: false, ct, jsonMode: true);

        var dto = AgentJson.TryParse<PrepDto>(result.Text);
        return new InterviewPrep(
            LikelyQuestions: dto?.LikelyQuestions ?? [],
            PrepNotes: dto?.PrepNotes ?? []);
    }
}
