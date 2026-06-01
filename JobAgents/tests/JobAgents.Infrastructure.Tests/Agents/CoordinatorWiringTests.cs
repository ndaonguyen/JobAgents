using FluentAssertions;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.EventBus;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAgents.Infrastructure.Tests.Agents;

public class CoordinatorWiringTests
{
    [Fact]
    public async Task Pipeline_matches_every_posting_but_only_expands_the_top_N()
    {
        var bus = new ChannelAgentEventBus();
        var runId = new RunId("wiring");
        var config = JobHuntConfig.Default with
        {
            TopMatchesToExpand = 3,
            MaxFanOutConcurrency = 2,
            IncludeDomains = ["itviec.com", "linkedin.com"],
        };
        var context = new AgentRunContext();

        var postings = Enumerable.Range(0, 5)
            .Select(i => new JobPosting($"Role {i}", $"Co {i}", "Remote", $"https://x/{i}", "summary"))
            .ToList();

        var search = new FakeSearchAgent(postings);
        // Score ascending with index, so ranking must reorder: posting 4 (score 40) ranks first.
        var match = new FakeResumeMatchAgent(scoreByIndex: i => i * 10);
        var company = new FakeCompanyAgent();
        var salary = new FakeSalaryAgent();
        var interview = new FakeInterviewAgent();

        var coordinator = new Coordinator(
            new FakeRunner(), search, match, company, salary, interview, bus,
            context, NullLogger<Coordinator>.Instance);

        var subscription = CollectAsync(bus, runId);
        await coordinator.RunAsync(new AgentRunRequest(runId, "resume", "prefs"), config);
        var events = await subscription;

        // Search ran once; every posting was matched; only the top 3 were expanded.
        search.Calls.Should().Be(1);
        match.Calls.Should().Be(5);
        company.Calls.Should().Be(3);
        salary.Calls.Should().Be(3);
        interview.Calls.Should().Be(3);

        events.OfType<JobMatchedEvent>().Should().HaveCount(5);
        events.OfType<CompanyResearchedEvent>().Should().HaveCount(3);

        var terminal = events.OfType<AgentFinishedEvent>().Single(e => e.AgentId == AgentId.System);
        var result = System.Text.Json.JsonSerializer.Deserialize<JobHuntResult>(terminal.FinalText, AgentJson.Options)!;
        result.Dossiers.Should().HaveCount(3);
        result.Dossiers.Select(d => d.Match.Score).Should().BeInDescendingOrder();
        result.Dossiers[0].Match.Score.Should().Be(40);
    }

    private static async Task<List<AgentEvent>> CollectAsync(ChannelAgentEventBus bus, RunId runId)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in bus.SubscribeAsync(runId))
            events.Add(evt);
        return events;
    }

    private sealed class FakeRunner : IAgentRunner
    {
        private const string CriteriaJson =
            """{ "roles": ["dev"], "locations": ["Remote"], "seniority": "Senior", "mustHaveSkills": [], "niceToHaveSkills": [], "remoteOnly": true, "salaryExpectation": null }""";

        public Task<AgentResult> RunAsync(RunId runId, AgentId agentId, string role, string systemPrompt,
            string userPrompt, string? modelOverride, bool useTools, CancellationToken ct)
            => Task.FromResult(new AgentResult(CriteriaJson, AgentUsage.Zero));
    }

    private sealed class FakeSearchAgent(IReadOnlyList<JobPosting> postings) : ISearchAgent
    {
        public int Calls;
        public Task<IReadOnlyList<JobPosting>> FindJobsAsync(RunId runId, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(postings);
        }
    }

    private sealed class FakeResumeMatchAgent(Func<int, int> scoreByIndex) : IResumeMatchAgent
    {
        public int Calls;
        public Task<JobMatch> MatchAsync(RunId runId, int index, string resumeText, JobPosting posting, JobHuntConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new JobMatch(posting, scoreByIndex(index), [], [], "ok"));
        }
    }

    private sealed class FakeCompanyAgent : ICompanyResearchAgent
    {
        public int Calls;
        public Task<CompanyInsight> ResearchAsync(RunId runId, int index, string company, JobHuntConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new CompanyInsight(company, "summary", [], []));
        }
    }

    private sealed class FakeSalaryAgent : ISalaryAnalysisAgent
    {
        public int Calls;
        public Task<SalaryEstimate> AnalyzeAsync(RunId runId, int index, JobPosting posting, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new SalaryEstimate(100, 120, 140, "USD", "test"));
        }
    }

    private sealed class FakeInterviewAgent : IInterviewPrepAgent
    {
        public int Calls;
        public Task<InterviewPrep> PrepareAsync(RunId runId, int index, JobPosting posting, JobMatch match, JobHuntConfig config, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new InterviewPrep(["q1"], ["note"]));
        }
    }
}
