using FluentAssertions;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace JobAgents.Infrastructure.Tests.Agents;

/// <summary>
/// Verifies the matcher's deterministic seniority down-rank wiring: a posting below the requested level
/// is capped (a soft down-rank, not a delete) and tagged with a gap; an at/above-level posting and an
/// unknown-level posting are left at the model's score. The model call is faked so this is free + exact.
/// </summary>
public sealed class ResumeMatchSeniorityTests
{
    private const int Cap = 45;

    // The resume names C# so the grounding guard keeps that matched skill; level capping is independent.
    private const string Resume = "Senior engineer, 10 years of C# and .NET backend systems.";

    private static ResumeMatchAgent Agent(int modelScore)
    {
        var runner = new FakeRunner(
            $$"""{ "score": {{modelScore}}, "matchedSkills": ["C#"], "gaps": ["needs Kafka"], "rationale": "ok" }""");
        return new ResumeMatchAgent(runner, Options.Create(new JobAgentsOptions()));
    }

    private static JobPosting Posting(string title) =>
        new(title, "Acme", "Remote", "https://x/1", "summary", Description: "Backend role in C#/.NET.");

    private static SearchCriteria Criteria(string seniority) => new(
        Roles: ["Backend Engineer"], Locations: ["Remote"], Seniority: seniority,
        MustHaveSkills: [], NiceToHaveSkills: [], WorkStyles: ["Remote"], SalaryExpectation: null);

    private static Task<JobMatch> MatchAsync(ResumeMatchAgent agent, JobPosting posting, SearchCriteria criteria) =>
        agent.MatchAsync(new RunId("t"), 0, Resume, posting, criteria, JobHuntConfig.Default, default);

    [Fact]
    public async Task Caps_the_score_when_the_posting_is_below_the_requested_level()
    {
        // Model says 88 for a Senior posting, but the user asked for Lead → capped.
        var match = await MatchAsync(Agent(88), Posting("Senior Backend Engineer"), Criteria("Lead"));

        match.Score.Should().Be(Cap);
        match.Gaps.Should().Contain(g => g.Contains("Below target seniority"));
    }

    [Fact]
    public async Task Does_not_raise_a_score_that_is_already_below_the_cap()
    {
        // A below-floor posting the model already scored under the cap keeps its lower score.
        var match = await MatchAsync(Agent(30), Posting("Senior Backend Engineer"), Criteria("Lead"));

        match.Score.Should().Be(30);
        match.Gaps.Should().Contain(g => g.Contains("Below target seniority"));
    }

    [Fact]
    public async Task Leaves_an_at_or_above_level_posting_untouched()
    {
        var match = await MatchAsync(Agent(88), Posting("Staff Backend Engineer"), Criteria("Lead"));

        match.Score.Should().Be(88);
        match.Gaps.Should().NotContain(g => g.Contains("Below target seniority"));
    }

    [Fact]
    public async Task Is_lenient_when_the_posting_title_has_no_level_word()
    {
        // "Backend Engineer" carries no level → not penalised, even with a Lead floor.
        var match = await MatchAsync(Agent(88), Posting("Backend Engineer"), Criteria("Lead"));

        match.Score.Should().Be(88);
        match.Gaps.Should().NotContain(g => g.Contains("Below target seniority"));
    }

    private sealed class FakeRunner(string json) : IAgentRunner
    {
        public Task<AgentResult> RunAsync(
            RunId runId, AgentId agentId, string role, string systemPrompt, string userPrompt,
            string? modelOverride, bool useTools, CancellationToken ct = default, bool jsonMode = false)
            => Task.FromResult(new AgentResult(json, AgentUsage.Zero));
    }
}
