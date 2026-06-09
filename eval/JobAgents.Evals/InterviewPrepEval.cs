using JobAgents.Application.Abstractions;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace JobAgents.Evals;

/// <summary>
/// Golden-case eval for the Interview-Preparation agent. Mirrors the matcher eval's shape (N trials,
/// majority vote) but the agent's output is free text, so each trial is checked two ways:
///   • structural / deterministic — sane item counts, no duplicate questions, nothing blank;
///   • LLM-as-judge — are the questions role-relevant, and do the prep notes address the known gaps?
/// Opt-in via `dotnet run -- interview`. Runs on the InterviewPrep model (OpenAI by default), no tools.
/// </summary>
internal static class InterviewPrepEval
{
    /// <summary>One labelled prep scenario: a posting + a fit assessment whose gaps the prep must cover.</summary>
    private sealed record PrepCase(string Name, JobPosting Posting, JobMatch Match);

    // Item-count bands: enough to be useful, not so many it's padding. A duplicate question or a blank
    // item is always a defect regardless of the band.
    private const int MinQuestions = 3, MaxQuestions = 15, MinNotes = 2;

    public static async Task<int> RunAsync(IServiceProvider provider)
    {
        var agent = provider.GetRequiredService<IInterviewPrepAgent>();
        var runner = provider.GetRequiredService<IAgentRunner>();
        var judge = new Judge(runner);
        var runId = RunId.New();

        const int trials = 3;
        var majority = (trials / 2) + 1;
        var cases = BuildCases();

        Console.WriteLine($"Running {cases.Count} interview-prep eval case(s) ({trials} trials/case, majority = {majority})…\n");
        Console.WriteLine(new string('─', 78));

        var allPassed = true;
        foreach (var c in cases)
        {
            int structureOk = 0, relevant = 0, gapsOk = 0;
            var sampleQuestion = "(none)";
            var notes = new List<string>();

            for (var t = 0; t < trials; t++)
            {
                var prep = await agent.PrepareAsync(runId, t, c.Posting, c.Match, JobHuntConfig.Default, default);

                if (IsStructurallySound(prep))
                    structureOk++;
                else
                    notes.Add("structure: bad counts / blank / duplicate question");

                var verdict = await judge.AuditInterviewPrepAsync(runId, c.Posting, c.Match.Gaps, prep, null, default);
                if (verdict.Relevant) relevant++;
                if (verdict.AddressesGaps) gapsOk++;
                if (!verdict.Relevant || !verdict.AddressesGaps)
                    notes.Add($"judge: {verdict.Note}");

                if (prep.LikelyQuestions.Count > 0)
                    sampleQuestion = prep.LikelyQuestions[0];
            }

            var structurePass = structureOk >= majority;
            var relevantPass = relevant >= majority;
            var gapsPass = gapsOk >= majority;
            var passed = structurePass && relevantPass && gapsPass;
            allPassed &= passed;

            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {c.Name}");
            Console.WriteLine($"      structure {structureOk}/{trials} {Mark(structurePass)}   relevant {relevant}/{trials} {Mark(relevantPass)}   addresses-gaps {gapsOk}/{trials} {Mark(gapsPass)}");
            Console.WriteLine($"      gaps: {(c.Match.Gaps.Count == 0 ? "(none)" : string.Join(", ", c.Match.Gaps))}");
            Console.WriteLine($"      e.g. question: {sampleQuestion}");
            if (!passed)
                foreach (var n in notes.Distinct().Take(4))
                    Console.WriteLine($"        ⚠ {n}");
            Console.WriteLine();
        }

        Console.WriteLine(new string('─', 78));
        Console.WriteLine(allPassed ? "All interview-prep cases passed." : "Some interview-prep cases failed.");
        return allPassed ? 0 : 1;
    }

    // Deterministic, model-free checks: sane counts, no blank items, no duplicate questions.
    private static bool IsStructurallySound(InterviewPrep prep)
    {
        if (prep.LikelyQuestions.Count is < MinQuestions or > MaxQuestions)
            return false;
        if (prep.PrepNotes.Count < MinNotes)
            return false;
        if (prep.LikelyQuestions.Concat(prep.PrepNotes).Any(string.IsNullOrWhiteSpace))
            return false;

        var distinctQuestions = prep.LikelyQuestions
            .Select(q => q.Trim().ToLowerInvariant())
            .Distinct()
            .Count();
        return distinctQuestions == prep.LikelyQuestions.Count;
    }

    private static string Mark(bool ok) => ok ? "✓" : "✗";

    private static IReadOnlyList<PrepCase> BuildCases() =>
    [
        // Strong fit, no gaps: prep should still be role-relevant; "addresses-gaps" is vacuously true.
        new PrepCase(
            "strong-fit-dotnet-backend",
            new JobPosting(
                Title: "Senior Backend Engineer (.NET)",
                Company: "Acme Cloud",
                Location: "Remote",
                Url: "https://example.com/jobs/senior-dotnet",
                Summary: "Senior backend role building .NET microservices on AWS with Kafka and DDD."),
            new JobMatch(
                Posting: new JobPosting("Senior Backend Engineer (.NET)", "Acme Cloud", "Remote", "https://example.com/jobs/senior-dotnet", ""),
                Score: 90,
                MatchedSkills: ["C#", ".NET", "Kafka", "AWS", "Microservices"],
                Gaps: [],
                Rationale: "Strong fit.")),

        // Clear gaps the prep MUST address: an iOS/Swift role for a backend candidate.
        new PrepCase(
            "gaps-ios-swift",
            new JobPosting(
                Title: "Senior iOS Engineer (Swift)",
                Company: "Mobile First",
                Location: "Hybrid - London",
                Url: "https://example.com/jobs/senior-ios",
                Summary: "Senior native iOS engineer building consumer apps in Swift and SwiftUI."),
            new JobMatch(
                Posting: new JobPosting("Senior iOS Engineer (Swift)", "Mobile First", "Hybrid - London", "https://example.com/jobs/senior-ios", ""),
                Score: 22,
                MatchedSkills: [],
                Gaps: ["Swift", "SwiftUI", "UIKit", "iOS SDK"],
                Rationale: "Weak fit; no native iOS experience.")),

        // Partial fit with a single sharp gap (Kubernetes) the prep should focus on.
        new PrepCase(
            "gap-platform-kubernetes",
            new JobPosting(
                Title: "Platform / DevOps Engineer",
                Company: "Orbit Infra",
                Location: "Remote",
                Url: "https://example.com/jobs/platform-devops",
                Summary: "Platform engineer owning a Kubernetes-based delivery platform; Terraform, AWS, Docker."),
            new JobMatch(
                Posting: new JobPosting("Platform / DevOps Engineer", "Orbit Infra", "Remote", "https://example.com/jobs/platform-devops", ""),
                Score: 60,
                MatchedSkills: ["Terraform", "AWS", "Docker"],
                Gaps: ["Kubernetes", "Helm"],
                Rationale: "Adjacent fit; missing the core Kubernetes platform skill.")),
    ];
}
