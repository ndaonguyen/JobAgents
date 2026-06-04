using System.Text.Json;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;

namespace JobAgents.Evals;

/// <summary>The judge's verdict on whether a match's claims are grounded in the resume.</summary>
public sealed record JudgeVerdict(bool Grounded, string[] Unsupported, string Note);

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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record VerdictDto(bool Grounded, List<string>? Unsupported, string? Note);
}
