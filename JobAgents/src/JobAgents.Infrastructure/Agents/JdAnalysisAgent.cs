using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Analysis;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Standalone gap-analysis agent. Unlike the job-hunt <see cref="ResumeMatchAgent"/> (which scores
/// many sourced postings quickly), this does one deep, advice-oriented analysis of a single JD the
/// user pasted: strengths, gaps with severity, missing keywords, CV tailoring and interview angles.
/// </summary>
public sealed class JdAnalysisAgent(IAgentRunner runner) : IJdAnalysisAgent
{
    private const string SystemPrompt =
        """
        You are a career coach and technical recruiter. Given a candidate's resume and ONE job
        description, produce an honest, specific gap analysis. Return ONLY a JSON object:
        {
          "overallScore": integer 0-100,
          "verdict": string,
          "matchedStrengths": string[],
          "gaps": [ { "requirement": string, "severity": "critical" | "moderate" | "minor", "advice": string } ],
          "missingKeywords": string[],
          "cvSuggestions": string[],
          "interviewTalkingPoints": string[],
          "summary": string
        }
        Guidance:
        - "overallScore": 85-100 strong fit, 70-84 good, 60-69 borderline, below 60 weak. Weigh the
          JD's must-have requirements and seniority heavily.
        - "verdict": one short sentence, e.g. "Strong fit with minor gaps in cloud tooling".
        - "matchedStrengths": concrete resume evidence that satisfies JD requirements — name the actual
          experience/skill, not generic praise.
        - "gaps": each a real JD requirement the candidate lacks or under-demonstrates. "severity" =
          how central it is to the role. "advice" = a concrete, actionable step to close or honestly
          present around the gap.
        - "missingKeywords": specific skills/tools/terms in the JD that are absent from the resume
          (useful for ATS keyword alignment).
        - "cvSuggestions": specific edits to tailor THIS resume to THIS JD — bullets to add/reword,
          experience to surface. Be concrete; reference real resume content.
        - "interviewTalkingPoints": angles the candidate should lead with, and how to address the gaps
          if asked.
        - "summary": 2-4 sentences of overall assessment.
        Base everything only on the resume and JD provided; never invent experience the resume lacks.
        """;

    private sealed record GapDto(string? Requirement, string? Severity, string? Advice);

    private sealed record AnalysisDto(
        int OverallScore,
        string? Verdict,
        List<string>? MatchedStrengths,
        List<GapDto>? Gaps,
        List<string>? MissingKeywords,
        List<string>? CvSuggestions,
        List<string>? InterviewTalkingPoints,
        string? Summary);

    public async Task<JdAnalysis> AnalyzeAsync(
        RunId runId, string resumeText, string jobDescription, string? modelOverride, CancellationToken ct)
    {
        var userPrompt =
            $"""
            CANDIDATE RESUME:
            {resumeText}

            JOB DESCRIPTION:
            {jobDescription}
            """;

        var result = await runner.RunAsync(
            runId, AgentId.JdAnalysis, "JD Analysis",
            SystemPrompt, userPrompt, modelOverride, useTools: false, ct, jsonMode: true);

        var dto = AgentJson.TryParse<AnalysisDto>(result.Text);
        if (dto is null)
            return JdAnalysis.Empty;

        var gaps = (dto.Gaps ?? [])
            .Select(g => new JdGap(
                Requirement: g.Requirement ?? string.Empty,
                Severity: NormalizeSeverity(g.Severity),
                Advice: g.Advice ?? string.Empty))
            .Where(g => g.Requirement.Length > 0)
            .ToList();

        return new JdAnalysis(
            OverallScore: Math.Clamp(dto.OverallScore, 0, 100),
            Verdict: string.IsNullOrWhiteSpace(dto.Verdict) ? "(no verdict produced)" : dto.Verdict,
            MatchedStrengths: dto.MatchedStrengths ?? [],
            Gaps: gaps,
            MissingKeywords: dto.MissingKeywords ?? [],
            CvSuggestions: dto.CvSuggestions ?? [],
            InterviewTalkingPoints: dto.InterviewTalkingPoints ?? [],
            Summary: dto.Summary ?? string.Empty);
    }

    private static string NormalizeSeverity(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "minor" => "minor",
            _ => "moderate",
        };
}
