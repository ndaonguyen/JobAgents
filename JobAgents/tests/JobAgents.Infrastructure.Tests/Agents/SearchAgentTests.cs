using FluentAssertions;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;

namespace JobAgents.Infrastructure.Tests.Agents;

public class SearchAgentTests
{
    private const string Postings =
        """
        [
          {"title":"Open Dev","company":"A","location":"Ho Chi Minh City","url":"https://x/1","summary":"great role"},
          {"title":"Stale Dev","company":"B","location":"Ho Chi Minh City","url":"https://x/2","summary":"No longer accepting applications"}
        ]
        """;

    [Fact]
    public async Task FindJobs_drops_closed_postings_when_open_only()
    {
        var agent = new SearchAgent(new FakeRunner(Postings));

        var result = await agent.FindJobsAsync(
            new RunId("r"), SearchCriteria.Empty, JobHuntConfig.Default with { OpenOnly = true }, default);

        result.Should().ContainSingle().Which.Title.Should().Be("Open Dev");
    }

    [Fact]
    public async Task FindJobs_keeps_all_postings_when_open_only_disabled()
    {
        var agent = new SearchAgent(new FakeRunner(Postings));

        var result = await agent.FindJobsAsync(
            new RunId("r"), SearchCriteria.Empty, JobHuntConfig.Default with { OpenOnly = false }, default);

        result.Should().HaveCount(2);
    }

    private sealed class FakeRunner(string text) : IAgentRunner
    {
        public Task<AgentResult> RunAsync(RunId runId, AgentId agentId, string role, string systemPrompt,
            string userPrompt, string? modelOverride, bool useTools, CancellationToken ct = default, bool jsonMode = false)
            => Task.FromResult(new AgentResult(text, AgentUsage.Zero));
    }
}
