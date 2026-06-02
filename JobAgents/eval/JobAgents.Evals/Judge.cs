using System.Text.Json;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;

namespace JobAgents.Evals;

/// <summary>The judge's verdict on whether a match's claims are grounded in the resume.</summary>
public sealed record JudgeVerdict(bool Grounded, string[] Unsupported, string Note);

/// <summary>
/// LLM-as-judge: a second model call that audits the matcher's output, checking that every skill it
/// claimed the resume demonstrates — and its rationale — is actually supported by the resume text.
/// Catches the "plausible but hallucinated" failure mode that a score band alone can't.
/// </summary>
public sealed class Judge(IAgentRunner runner)
{
    private static readonly AgentId JudgeAgent = new("eval-judge");

    private const string SystemPrompt =
        """
        You are a strict evaluation judge detecting HALLUCINATIONS in an automated resume matcher.
        You are given a candidate RESUME, the matcher's MATCHED SKILLS (skills it claims the resume
        demonstrates), and its RATIONALE. A hallucination is any claim that the candidate HAS or
        DEMONSTRATES a skill/experience that the resume does NOT actually support.
        Return ONLY a JSON object: { "grounded": boolean, "unsupported": string[], "note": string }
        Rules:
        - Every entry in MATCHED SKILLS must be clearly evidenced in the resume; if not, it is unsupported.
        - In the RATIONALE, ONLY flag statements asserting the candidate POSSESSES something the resume
          lacks. DO NOT flag statements saying the candidate LACKS, is MISSING, has NO, or would need a
          skill — those are correct gap observations, not hallucinations.
        - "grounded" is true when there are no hallucinated possession-claims.
        - "unsupported" lists only the hallucinated claims; "note" is one short sentence.
        Judge strictly against the resume text only.
        """;

    public async Task<JudgeVerdict> AuditAsync(
        RunId runId, string resume, IReadOnlyList<string> matchedSkills, string rationale,
        string? model, CancellationToken ct)
    {
        if (matchedSkills.Count == 0 && string.IsNullOrWhiteSpace(rationale))
            return new JudgeVerdict(true, [], "Nothing to verify.");

        var userPrompt =
            $"""
            RESUME:
            {resume}

            CLAIMED MATCHED SKILLS:
            {(matchedSkills.Count == 0 ? "(none)" : string.Join(", ", matchedSkills))}

            RATIONALE:
            {rationale}
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
