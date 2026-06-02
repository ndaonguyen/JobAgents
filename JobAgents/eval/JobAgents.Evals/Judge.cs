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
        You are a strict evaluation judge. Given a candidate RESUME, the SKILLS an automated matcher
        claimed the resume demonstrates, and the matcher's RATIONALE, decide whether every claim is
        genuinely supported by the resume text. Return ONLY a JSON object:
        { "grounded": boolean, "unsupported": string[], "note": string }
        - "grounded" is true ONLY if every claimed skill and every factual statement in the rationale
          is clearly supported by the resume.
        - "unsupported" lists any claimed skills or rationale statements the resume does NOT support
          (i.e. hallucinations).
        - "note": one short sentence explaining your verdict.
        Judge strictly against the resume text only; do not credit skills that aren't stated.
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
