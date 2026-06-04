using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobAgents.Evals;

/// <summary>
/// Runs a set of <see cref="MatchCase"/>s through the live matcher and prints the scorecard. Shared by
/// the default golden-set run and the <c>feedback-cases</c> run so both score identically: each case is
/// run several times (LLMs are non-deterministic) and aggregated by median score + majority vote.
/// </summary>
internal static class EvalRunner
{
    public static async Task<int> RunAsync(IServiceProvider provider, IReadOnlyList<MatchCase> cases, string label)
    {
        var options = provider.GetRequiredService<IOptions<JobAgentsOptions>>().Value;
        var matcher = provider.GetRequiredService<IResumeMatchAgent>();
        var runner = provider.GetRequiredService<IAgentRunner>();
        var bus = provider.GetRequiredService<IAgentEventBus>();
        var judge = new Judge(runner);

        // One run id for the whole eval; a background reader sums token cost off the event bus.
        var evalRunId = RunId.New();
        var costReader = Task.Run(async () =>
        {
            decimal cost = 0m;
            var tokens = 0;
            await foreach (var evt in bus.SubscribeAsync(evalRunId))
                if (evt is AgentFinishedEvent f)
                {
                    cost += f.EstimatedCostUsd ?? 0m;
                    tokens += f.TokensIn + f.TokensOut;
                }
            return (cost, tokens);
        });

        Console.WriteLine(
            $"Running {cases.Count} {label} case(s) on matcher '{options.Anthropic.Model}' " +
            $"(judge '{options.OpenAi.Model}')…\n");

        // LLMs are non-deterministic, so a single run can't separate signal from noise. Run each case
        // several times and aggregate (median score + majority vote) — the basis for trustworthy tuning.
        const int trials = 3;
        var majority = (trials / 2) + 1;

        var results = new List<CaseResult>();
        foreach (var c in cases)
        {
            var scores = new List<int>();
            int inBand = 0, skillsOk = 0, grounded = 0;
            var sampleMatched = "(none)";
            var sampleRationale = "(none)";
            var groundingFlags = new List<string>();

            for (var t = 0; t < trials; t++)
            {
                var match = await matcher.MatchAsync(evalRunId, t, c.Resume, c.Posting, c.Criteria, JobHuntConfig.Default, default);
                scores.Add(match.Score);
                if (match.Score >= c.MinScore && match.Score <= c.MaxScore)
                    inBand++;

                var matchedJoined = string.Join(" | ", match.MatchedSkills);
                var allSkillsPresent = c.ExpectedMatchedSkills
                    .All(s => matchedJoined.Contains(s, StringComparison.OrdinalIgnoreCase));
                if (allSkillsPresent)
                    skillsOk++;

                var verdict = await judge.AuditAsync(evalRunId, c.Resume, match.MatchedSkills, null, default);
                if (verdict.Grounded)
                    grounded++;
                else
                    groundingFlags.AddRange(verdict.Unsupported.Length > 0 ? verdict.Unsupported : [verdict.Note]);

                if (match.MatchedSkills.Count > 0)
                    sampleMatched = matchedJoined;
                if (!verdict.Grounded)
                    sampleRationale = matchedJoined;
            }

            results.Add(new CaseResult(
                c,
                MedianScore: scores.OrderBy(s => s).ElementAt(scores.Count / 2),
                Scores: scores,
                InBand: inBand,
                SkillsOk: skillsOk,
                Grounded: grounded,
                Trials: trials,
                Majority: majority,
                SampleMatched: sampleMatched,
                SampleRationale: sampleRationale,
                GroundingFlags: groundingFlags));
        }

        // End the cost subscription cleanly (terminal System event flushes the FIFO reader).
        await bus.PublishAsync(new AgentFinishedEvent(evalRunId, AgentId.System, "", 0, 0, 0m, DateTime.UtcNow));
        var (totalCost, totalTokens) = await costReader;

        // ── Scorecard ──────────────────────────────────────────────────────────────────────────────
        Console.WriteLine($"Eval scorecard  ({trials} trials/case, majority = {majority})");
        Console.WriteLine(new string('─', 78));
        foreach (var r in results)
        {
            Console.WriteLine($"{(r.Passed ? "PASS" : "FAIL")}  {r.Case.Name}");
            Console.WriteLine(
                $"      score median {r.MedianScore,3}  runs [{string.Join(",", r.Scores)}]  expected [{r.Case.MinScore}-{r.Case.MaxScore}]" +
                $"  in-band {r.InBand}/{r.Trials} {Mark(r.ScorePass)}");
            Console.WriteLine(
                $"      target {r.Case.TargetScore,3}  abs error {r.AbsError,3}  (lower is better)");
            if (r.Case.ExpectedMatchedSkills.Length > 0)
                Console.WriteLine($"      skills {r.SkillsOk}/{r.Trials} {Mark(r.SkillsPass)}   e.g. matched: {r.SampleMatched}");
            Console.WriteLine($"      grounded {r.Grounded}/{r.Trials} {Mark(r.GroundedPass)}");
            if (!r.GroundedPass && r.GroundingFlags.Count > 0)
            {
                foreach (var flag in r.GroundingFlags.Distinct().Take(6))
                    Console.WriteLine($"        ⚠ judge flagged skill: {flag}");
                Console.WriteLine($"        ↳ claimed skills were: {r.SampleRationale}");
            }
            Console.WriteLine();
        }

        var passed = results.Count(r => r.Passed);
        var mae = results.Count == 0 ? 0 : results.Average(r => r.AbsError);
        Console.WriteLine(new string('─', 78));
        Console.WriteLine(
            $"Passed {passed}/{results.Count}   score MAE: {mae:0.0}   tokens: {totalTokens:N0}   est. cost: {totalCost:$0.0000}");

        return passed == results.Count ? 0 : 1;
    }

    private static string Mark(bool ok) => ok ? "✓" : "✗";
}

internal sealed record CaseResult(
    MatchCase Case,
    int MedianScore,
    List<int> Scores,
    int InBand,
    int SkillsOk,
    int Grounded,
    int Trials,
    int Majority,
    string SampleMatched,
    string SampleRationale,
    List<string> GroundingFlags)
{
    public int AbsError => Math.Abs(MedianScore - Case.TargetScore);
    public bool ScorePass => InBand >= Majority;
    public bool SkillsPass => Case.ExpectedMatchedSkills.Length == 0 || SkillsOk >= Majority;
    public bool GroundedPass => Grounded >= Majority;
    public bool Passed => ScorePass && SkillsPass && GroundedPass;
}
