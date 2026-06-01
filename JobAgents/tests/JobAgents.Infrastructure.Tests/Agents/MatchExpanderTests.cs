using FluentAssertions;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.EventBus;

namespace JobAgents.Infrastructure.Tests.Agents;

public class MatchExpanderTests
{
    [Fact]
    public async Task ExpandAsync_runs_all_three_specialists_and_returns_a_full_dossier()
    {
        var bus = new ChannelAgentEventBus();
        var expander = new MatchExpander(
            new FakeCompany(), new FakeSalary(), new FakeInterview(), bus, new AgentRunContext());

        var posting = new JobPosting("Dev", "Acme", "Remote", "https://x/1", "summary");
        var match = new JobMatch(posting, 80, ["C#"], ["k8s"], "ok");

        var dossier = await expander.ExpandAsync(match, SearchCriteria.Empty, JobHuntConfig.Default);

        dossier.Match.Should().Be(match);
        dossier.Company.Should().NotBeNull();
        dossier.Salary.Should().NotBeNull();
        dossier.Interview.Should().NotBeNull();
    }

    private sealed class FakeCompany : ICompanyResearchAgent
    {
        public Task<CompanyInsight> ResearchAsync(RunId runId, int index, string company, JobHuntConfig config, CancellationToken ct)
            => Task.FromResult(new CompanyInsight(company, "summary", [], []));
    }

    private sealed class FakeSalary : ISalaryAnalysisAgent
    {
        public Task<SalaryEstimate> AnalyzeAsync(RunId runId, int index, JobPosting posting, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
            => Task.FromResult(new SalaryEstimate(100, 120, 140, "USD", "test"));
    }

    private sealed class FakeInterview : IInterviewPrepAgent
    {
        public Task<InterviewPrep> PrepareAsync(RunId runId, int index, JobPosting posting, JobMatch match, JobHuntConfig config, CancellationToken ct)
            => Task.FromResult(new InterviewPrep(["q1"], ["note"]));
    }
}
