using System.Text.Json;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;

namespace JobAgents.Evals;

/// <summary>The judge's verdict on whether a match's claims are grounded in the resume.</summary>
public sealed record JudgeVerdict(bool Grounded, string[] Unsupported, string Note);

/// <summary>The judge's verdict on a generated interview-prep set (quality, not grounding).</summary>
public sealed record PrepVerdict(bool Relevant, bool AddressesGaps, string Note);

/// <summary>
/// LLM-as-judge: a second model call that audits the matcher's CLAIMED MATCHED SKILLS — checking each
/// is actually evidenced in the resume text. Catches the "plausible but hallucinated skill" failure
/// mode that a score band alone can't.
///
/// It deliberately does NOT inspect the free-text rationale: a weak judge model (gpt-4o-mini) reads a
/// skill name inside a gap sentence ("no evidence of Swift") and flags it as a possession-claim, which
/// produced almost all of this eval's grounding false-positives. The matched-skills list is the only
/// place a hallucinated possession-claim can be checked unambiguously, so that is all we judge.
/// </summary>
public sealed class Judge(IAgentRunner runner)
{
    private static readonly AgentId JudgeAgent = new("eval-judge");

    private const string SystemPrompt =
        """
        You are a strict evaluation judge detecting HALLUCINATED SKILLS in an automated resume matcher.
        You are given a candidate RESUME and the matcher's CLAIMED MATCHED SKILLS — the skills it claims
        the resume demonstrates. Decide whether EACH claimed skill is actually evidenced in the resume.
        Return ONLY a JSON object: { "grounded": boolean, "unsupported": string[], "note": string }
        Rules:
        - A skill is SUPPORTED if the resume names it or unambiguously shows it (e.g. "ASP.NET Core"
          supports ".NET"; "Apache Kafka" supports "Kafka").
        - A skill is UNSUPPORTED (hallucinated) only if NOTHING in the resume evidences it.
        - Past, earlier-career, brief, or minor mentions STILL count as evidenced. Judge only whether
          the resume names/shows the skill — NOT how recent, strong, or role-relevant it is.
        - "unsupported" lists every claimed skill that is not evidenced; "grounded" is true only when
          "unsupported" is empty.
        - "note" is one short sentence.
        Judge strictly against the resume text only. There is no job posting or rationale to consider —
        evaluate ONLY the claimed matched skills against the resume.
        """;

    public async Task<JudgeVerdict> AuditAsync(
        RunId runId, string resume, IReadOnlyList<string> matchedSkills, string? model, CancellationToken ct)
    {
        // No claimed skills → nothing can be hallucinated. (The matcher's own GroundMatchedSkills filter
        // already drops skills absent from the resume, so this case is also the "honest empty" case.)
        if (matchedSkills.Count == 0)
            return new JudgeVerdict(true, [], "No matched skills to verify.");

        var userPrompt =
            $"""
            RESUME:
            {resume}

            CLAIMED MATCHED SKILLS:
            {string.Join(", ", matchedSkills)}
            """;

        var result = await runner.RunAsync(
            runId, JudgeAgent, "Eval Judge",
            SystemPrompt, userPrompt, model, useTools: false, ct, jsonMode: true);

        try
        {
            var dto = JsonSerializer.Deserialize<VerdictDto>(result.Text, JsonOptions);
            return new JudgeVerdict(
                dto?.Grounded ?? false,
                dto?.Unsupported?.ToArray() ?? [],
                dto?.Note ?? "(no verdict produced)");
        }
        catch (JsonException)
        {
            return new JudgeVerdict(false, [], "Judge returned unparseable output.");
        }
    }

    private const string InterviewSystemPrompt =
        """
        You are a strict evaluation judge for an automated interview-preparation agent. You are given a
        JOB (title + summary), the candidate's KNOWN GAPS for that job, and the agent's generated LIKELY
        QUESTIONS and PREP NOTES. Decide two things:
        - "relevant": are the LIKELY QUESTIONS plausibly questions for THIS role — not generic filler
          unrelated to the job's domain and seniority?
        - "addressesGaps": do the PREP NOTES give concrete advice on the candidate's listed GAPS?
          If the KNOWN GAPS list is "(none)", set this to true (there is nothing to address).
        Return ONLY a JSON object: { "relevant": boolean, "addressesGaps": boolean, "note": string }
        "note" is one short sentence. Judge only the text given.
        """;

    /// <summary>
    /// LLM-as-judge for interview prep: a quality check (not a grounding check). Verifies the questions
    /// are role-relevant and the prep notes actually speak to the candidate's stated gaps — the failure
    /// modes (generic boilerplate questions, notes that ignore the gaps) that item counts can't catch.
    /// </summary>
    public async Task<PrepVerdict> AuditInterviewPrepAsync(
        RunId runId, JobPosting posting, IReadOnlyList<string> gaps, InterviewPrep prep, string? model, CancellationToken ct)
    {
        var userPrompt =
            $"""
            JOB:
            Title: {posting.Title}
            Summary: {posting.Summary}

            KNOWN GAPS:
            {(gaps.Count == 0 ? "(none)" : string.Join(", ", gaps))}

            LIKELY QUESTIONS:
            {string.Join("\n", prep.LikelyQuestions.Select(q => "- " + q))}

            PREP NOTES:
            {string.Join("\n", prep.PrepNotes.Select(n => "- " + n))}
            """;

        var result = await runner.RunAsync(
            runId, JudgeAgent, "Eval Judge (Interview)",
            InterviewSystemPrompt, userPrompt, model, useTools: false, ct, jsonMode: true);

        try
        {
            var dto = JsonSerializer.Deserialize<PrepVerdictDto>(result.Text, JsonOptions);
            return new PrepVerdict(
                dto?.Relevant ?? false,
                dto?.AddressesGaps ?? false,
                dto?.Note ?? "(no verdict produced)");
        }
        catch (JsonException)
        {
            return new PrepVerdict(false, false, "Judge returned unparseable output.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record VerdictDto(bool Grounded, List<string>? Unsupported, string? Note);

    private sealed record PrepVerdictDto(bool Relevant, bool AddressesGaps, string? Note);
}
