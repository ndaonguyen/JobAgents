using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobAgents.Infrastructure.Agents;

public interface IResumeMatchAgent
{
    Task<JobMatch> MatchAsync(
        RunId runId, int index, string resumeText, JobPosting posting, SearchCriteria criteria,
        JobHuntConfig config, CancellationToken ct);
}

/// <summary>Scores a single posting against the candidate's resume and explains the fit.</summary>
public sealed class ResumeMatchAgent(IAgentRunner runner, IOptions<JobAgentsOptions> options) : IResumeMatchAgent
{
    private readonly string _defaultModel = options.Value.Anthropic.Model;

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
        If the resume does not evidence the posting's primary required skill/technology (the one
        central to the role), the score must NOT exceed 70 — strong secondary overlaps cannot
        compensate for a missing core requirement.
        - "matchedSkills": ONLY skills the RESUME explicitly demonstrates AND the posting asks for.
          Never list a skill just because the posting requires it — if the resume does not clearly
          evidence that skill, it does NOT belong here; put it in "gaps" instead. Every entry must be
          backed by something actually written in the resume.
          NEVER include inferred, assumed, "transferable", or parenthetically-qualified skills. If you
          find yourself writing "(inferred from …)", "(likely)", "(transferable)", or grouping several
          technologies under an umbrella the resume never names, that skill is NOT a match — move it to
          "gaps". Each entry must be a skill literally named or unambiguously shown in the resume, listed
          as a plain skill name with no qualifiers, hedges, or parentheses.
        - "gaps": specific requirements the posting asks for that the candidate lacks or
          under-demonstrates in the resume (this is where missing required skills go).
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
            - Work modes: {Join(criteria.WorkStyles)}

            JOB POSTING:
            Title: {posting.Title}
            Company: {posting.Company}
            Location: {posting.Location}
            Summary: {posting.Summary}
            Description: {description}
            URL: {posting.Url}
            """;

        // Matching defaults to Claude (Anthropic.Model); a per-run ResumeMatchModel override still wins.
        var model = config.ResumeMatchModel ?? _defaultModel;
        var result = await runner.RunAsync(
            runId, AgentId.ResumeMatch(index), "Resume Matching",
            SystemPrompt, userPrompt, model, useTools: false, ct, jsonMode: true);

        var dto = AgentJson.TryParse<MatchDto>(result.Text);
        var score = Math.Clamp(dto?.Score ?? 0, 0, 100);
        var gaps = dto?.Gaps?.ToList() ?? [];

        // Deterministic seniority down-rank: the candidate's resume may genuinely FIT a role below their
        // target level (a senior dev fits a "Senior" posting), so the model scores it high — but the user
        // asked for a higher level. Cap such postings so they sink below the fit bar instead of crowding
        // out on-level roles. Soft (a cap, not a delete) and lenient: only fires when the posting's title
        // clearly names a lower level than requested. See Seniority.IsBelowFloor.
        if (Seniority.IsBelowFloor(posting, criteria.Seniority))
        {
            score = Math.Min(score, BelowSeniorityFloorCap);
            gaps.Insert(0,
                $"Below target seniority (role reads as {Seniority.DetectFromPosting(posting)}, " +
                $"you asked for {Seniority.Parse(criteria.Seniority)}+)");
        }

        return new JobMatch(
            Posting: posting,
            Score: score,
            // Deterministic grounding guard: smaller models sometimes copy the posting's required
            // skills into matchedSkills even when the resume never shows them. Drop any matched skill
            // whose name doesn't actually appear in the resume text, so the list can't claim skills
            // the candidate hasn't evidenced. Score and gaps are left untouched.
            MatchedSkills: GroundMatchedSkills(dto?.MatchedSkills, resumeText),
            Gaps: gaps,
            Rationale: dto?.Rationale ?? "(no rationale produced)");
    }

    // A posting below the requested seniority is capped to this score so it falls under the typical
    // fit bar without being hard-deleted (a strong-but-off-level role can still appear if the user
    // lowers their minimum-fit threshold).
    private const int BelowSeniorityFloorCap = 45;

    /// <summary>
    /// Keeps only skills whose name is actually present in the resume. Matching is case- and
    /// punctuation-insensitive (so "ASP.NET Core" matches "asp net core"); short symbolic skills like
    /// "C#" must appear as a whole word to avoid spurious substring hits, longer names match anywhere.
    /// </summary>
    private static List<string> GroundMatchedSkills(List<string>? skills, string resumeText)
    {
        if (skills is null || skills.Count == 0)
            return [];

        var normalizedResume = Normalize(resumeText);
        var paddedResume = $" {normalizedResume} ";

        var grounded = new List<string>();
        foreach (var skill in skills)
        {
            var normalized = Normalize(skill);
            if (normalized.Length == 0)
                continue;

            var present = normalized.Length <= 2
                ? paddedResume.Contains($" {normalized} ", StringComparison.Ordinal) // whole-word for "C#", "Go", …
                : normalizedResume.Contains(normalized, StringComparison.Ordinal);

            if (present)
                grounded.Add(skill);
        }

        return grounded;
    }

    /// <summary>Lower-cases and collapses every run of non-alphanumeric characters to a single space.</summary>
    private static string Normalize(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(any)" : string.Join(", ", values);

    private static string Or(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(any)" : value;
}
