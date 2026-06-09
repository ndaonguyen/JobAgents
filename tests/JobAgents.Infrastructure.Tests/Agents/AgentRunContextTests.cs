using FluentAssertions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;

namespace JobAgents.Infrastructure.Tests.Agents;

public class AgentRunContextTests
{
    [Fact]
    public async Task Concurrent_flows_do_not_leak_their_run_context()
    {
        var context = new AgentRunContext();

        // Each flow runs on its own task (as fanned-out agents do), so its AsyncLocal writes are
        // isolated from siblings.
        Task<(string run, string agent)> Flow(int i) => Task.Run(async () =>
        {
            context.Set(new RunId($"run-{i}"), AgentId.ResumeMatch(i));
            await Task.Delay(10);
            return (context.CurrentRun!.Value.Value, context.CurrentAgent!.Value.Value);
        });

        var results = await Task.WhenAll(Flow(1), Flow(2), Flow(3));

        results.Should().BeEquivalentTo(new[]
        {
            ("run-1", "resume-match-1"),
            ("run-2", "resume-match-2"),
            ("run-3", "resume-match-3"),
        });
    }

    [Fact]
    public async Task IncludeDomains_set_on_the_run_flow_down_to_child_work()
    {
        var context = new AgentRunContext();
        context.IncludeDomains = ["itviec.com", "linkedin.com"];

        // A child task (as the search plugin invocation effectively is) inherits the domains.
        var seen = await Task.Run(() =>
        {
            return context.IncludeDomains;
        });

        seen.Should().BeEquivalentTo("itviec.com", "linkedin.com");
    }
}
