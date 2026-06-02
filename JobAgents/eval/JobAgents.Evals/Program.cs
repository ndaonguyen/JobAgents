using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;
using JobAgents.Evals;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.Configuration;
using JobAgents.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ── Config + DI ────────────────────────────────────────────────────────────────────────────────
// Reuses the Web app's user-secrets (same UserSecretsId) so the OpenAI / Tavily keys just work.
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddInfrastructure(config);
using var provider = services.BuildServiceProvider();

var options = provider.GetRequiredService<IOptions<JobAgentsOptions>>().Value;
if (string.IsNullOrWhiteSpace(options.OpenAi.ApiKey))
{
    Console.Error.WriteLine(
        "No OpenAI API key found. Set it via user-secrets (JobAgents:OpenAi:ApiKey) on the " +
        "jobagents-web-dev secret store, or the JobAgents__OpenAi__ApiKey environment variable.");
    return 2;
}

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

Console.WriteLine($"Running {GoldenCases.Matches.Count} match eval case(s) on model '{options.OpenAi.Model}'…\n");

var results = new List<CaseResult>();
foreach (var c in GoldenCases.Matches)
{
    var match = await matcher.MatchAsync(evalRunId, 0, c.Resume, c.Posting, c.Criteria, JobHuntConfig.Default, default);

    var scorePass = match.Score >= c.MinScore && match.Score <= c.MaxScore;
    var matchedJoined = string.Join(" | ", match.MatchedSkills);
    var missingSkills = c.ExpectedMatchedSkills
        .Where(s => !matchedJoined.Contains(s, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var skillsPass = missingSkills.Length == 0;

    var verdict = await judge.AuditAsync(
        evalRunId, c.Resume, match.MatchedSkills, match.Rationale, model: null, default);

    results.Add(new CaseResult(c, match.Score, scorePass, missingSkills, skillsPass, verdict));
}

// End the cost subscription cleanly (terminal System event flushes the FIFO reader).
await bus.PublishAsync(new AgentFinishedEvent(evalRunId, AgentId.System, "", 0, 0, 0m, DateTime.UtcNow));
var (totalCost, totalTokens) = await costReader;

// ── Scorecard ──────────────────────────────────────────────────────────────────────────────────
Console.WriteLine("Eval scorecard");
Console.WriteLine(new string('─', 78));
foreach (var r in results)
{
    Console.WriteLine($"{(r.Passed ? "PASS" : "FAIL")}  {r.Case.Name}");
    Console.WriteLine(
        $"      score {r.Score,3}  expected [{r.Case.MinScore}-{r.Case.MaxScore}]  {Mark(r.ScorePass)}");
    if (r.Case.ExpectedMatchedSkills.Length > 0)
        Console.WriteLine(
            $"      skills {Mark(r.SkillsPass)}" +
            (r.MissingSkills.Length > 0 ? $"  missing: {string.Join(", ", r.MissingSkills)}" : ""));
    Console.WriteLine(
        $"      grounded {Mark(r.Verdict.Grounded)}  {r.Verdict.Note}" +
        (r.Verdict.Unsupported.Length > 0 ? $"  [unsupported: {string.Join(", ", r.Verdict.Unsupported)}]" : ""));
    Console.WriteLine();
}

var passed = results.Count(r => r.Passed);
Console.WriteLine(new string('─', 78));
Console.WriteLine($"Passed {passed}/{results.Count}   tokens: {totalTokens:N0}   est. cost: {totalCost:$0.0000}");

return passed == results.Count ? 0 : 1;

static string Mark(bool ok) => ok ? "✓" : "✗";

internal sealed record CaseResult(
    MatchCase Case,
    int Score,
    bool ScorePass,
    string[] MissingSkills,
    bool SkillsPass,
    JudgeVerdict Verdict)
{
    public bool Passed => ScorePass && SkillsPass && Verdict.Grounded;
}
