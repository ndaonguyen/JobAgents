using FluentAssertions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Domain.Tests;

public class IdAndEventTests
{
    [Fact]
    public void RunId_New_generates_unique_ids()
    {
        var a = RunId.New();
        var b = RunId.New();

        a.Should().NotBe(b);
        a.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AgentId_factories_are_indexed_and_distinct()
    {
        AgentId.ResumeMatch(0).Should().NotBe(AgentId.ResumeMatch(1));
        AgentId.ResumeMatch(2).Value.Should().Be("resume-match-2");
        AgentId.CompanyResearch(1).Value.Should().Be("company-research-1");
        AgentId.System.Should().NotBe(AgentId.Coordinator);
    }

    [Fact]
    public void Event_kinds_are_stable_discriminators()
    {
        var runId = RunId.New();
        var now = DateTime.UtcNow;

        new AgentStartedEvent(runId, AgentId.Search, "Search", now).Kind.Should().Be("agent.started");
        new AgentTokenEvent(runId, AgentId.Search, "x", now).Kind.Should().Be("agent.token");
        new AgentFinishedEvent(runId, AgentId.System, "done", 1, 2, 0.01m, now).Kind.Should().Be("agent.finished");
        new AgentErrorEvent(runId, AgentId.System, "boom", now).Kind.Should().Be("agent.error");
        new ToolCalledEvent(runId, AgentId.Search, "Web.search", "{}", now).Kind.Should().Be("tool.called");
        new ToolResultEvent(runId, AgentId.Search, "Web.search", "[]", 5, now).Kind.Should().Be("tool.result");

        var posting = new JobPosting("Dev", "Acme", "Remote", "https://x", "summary");
        new JobsFoundEvent(runId, AgentId.Search, [posting], now).Kind.Should().Be("jobs.found");
        new JobMatchedEvent(runId, AgentId.ResumeMatch(0),
            new JobMatch(posting, 80, [], [], "ok"), now).Kind.Should().Be("job.matched");
    }
}
