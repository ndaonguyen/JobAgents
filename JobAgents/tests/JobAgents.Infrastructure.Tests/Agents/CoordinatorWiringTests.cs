using FluentAssertions;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.EventBus;
using JobAgents.Infrastructure.Sourcing;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobAgents.Infrastructure.Tests.Agents;

public class CoordinatorWiringTests
{
    [Fact]
    public async Task Pipeline_returns_all_qualifying_matches_and_expands_only_the_top_N()
    {
        var bus = new ChannelAgentEventBus();
        var runId = new RunId("wiring");
        var config = JobHuntConfig.Default with
        {
            TopMatchesToExpand = 2,
            MinMatchScore = 60,
            MaxFanOutConcurrency = 2,
            IncludeDomains = ["itviec.com", "linkedin.com"],
        };
        var context = new AgentRunContext();

        // Scores by posting index; only those >= 60 should survive (90, 80, 70, 65 → 4 of 6).
        var scores = new[] { 90, 80, 70, 50, 40, 65 };
        var postings = Enumerable.Range(0, scores.Length)
            .Select(i => new JobPosting($"Role {i}", $"Co {i}", "Remote", $"https://x/{i}", "summary"))
            .ToList();

        var search = new FakeSearchAgent(postings);
        var match = new FakeResumeMatchAgent(scoreByIndex: i => scores[i]);
        var company = new FakeCompanyAgent();
        var salary = new FakeSalaryAgent();
        var interview = new FakeInterviewAgent();

        var coordinator = new Coordinator(
            new FakeRunner(), search, match, company, salary, interview, bus,
            context, new RunUsageAccumulator(), new WebSearchAccumulator(), new NullPostingStore(), NullLogger<Coordinator>.Instance);

        var subscription = CollectAsync(bus, runId);
        await coordinator.RunAsync(new AgentRunRequest(runId, "resume", "prefs"), config);
        var events = await subscription;

        // Every posting is matched; only the top 2 qualifying matches are expanded.
        search.Calls.Should().Be(1);
        match.Calls.Should().Be(6);
        company.Calls.Should().Be(2);
        salary.Calls.Should().Be(2);
        interview.Calls.Should().Be(2);

        events.OfType<JobMatchedEvent>().Should().HaveCount(6);

        var terminal = events.OfType<AgentFinishedEvent>().Single(e => e.AgentId == AgentId.System);
        var result = System.Text.Json.JsonSerializer.Deserialize<JobHuntResult>(terminal.FinalText, AgentJson.Options)!;

        // All 4 matches >= 60 are returned, ranked; only the top 2 carry expansion data.
        result.Dossiers.Select(d => d.Match.Score).Should().Equal(90, 80, 70, 65);
        result.Dossiers.Take(2).Should().OnlyContain(d => d.Company != null);
        result.Dossiers.Skip(2).Should().OnlyContain(d => d.Company == null);
    }

    [Fact]
    public async Task Top_matches_sharing_a_company_or_salary_key_are_researched_once()
    {
        var bus = new ChannelAgentEventBus();
        var runId = new RunId("dedup");
        var config = JobHuntConfig.Default with
        {
            TopMatchesToExpand = 3,
            MinMatchScore = 60,
            MaxFanOutConcurrency = 3,
        };

        // Three qualifying top matches, all distinct under title+company dedup. The COMPANY dedup pair
        // and the SALARY dedup pair are deliberately different pairs:
        //   idx0 & idx2 share the employer ("Acme")             → one company lookup.
        //   idx0 & idx1 share the salary key (title+location+seniority: "Senior Dev"/"Remote"/"Senior")
        //                                                        → one salary lookup.
        // Seniority comes from the parsed criteria (FakeRunner → "Senior") and is constant across all.
        var postings = new List<JobPosting>
        {
            new("Senior Dev", "Acme", "Remote", "https://x/0", "summary"),
            new("Senior Dev", "Beta", "Remote", "https://x/1", "summary"),
            new("Staff Dev", "Acme", "London", "https://x/2", "summary"),
        };
        var scores = new[] { 90, 85, 80 };

        var search = new FakeSearchAgent(postings);
        var match = new FakeResumeMatchAgent(scoreByIndex: i => scores[i]);
        var company = new FakeCompanyAgent();
        var salary = new FakeSalaryAgent();
        var interview = new FakeInterviewAgent();

        var coordinator = new Coordinator(
            new FakeRunner(), search, match, company, salary, interview, bus,
            new AgentRunContext(), new RunUsageAccumulator(), new WebSearchAccumulator(),
            new NullPostingStore(), NullLogger<Coordinator>.Instance);

        var subscription = CollectAsync(bus, runId);
        await coordinator.RunAsync(new AgentRunRequest(runId, "resume", "prefs"), config);
        await subscription;

        // Company research is deduped by employer: Acme (idx0, idx2) once + Beta once → 2 calls, not 3.
        company.Calls.Should().Be(2);
        // Salary is deduped by role + location + seniority: idx0 & idx1 (Senior Dev / Remote / Senior)
        // share one lookup; idx2 (Staff Dev / London) is distinct → 2 calls, not 3.
        salary.Calls.Should().Be(2);
        // Interview prep has no dedup — it is tailored per match → one call per expanded match.
        interview.Calls.Should().Be(3);
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
            string userPrompt, string? modelOverride, bool useTools, CancellationToken ct = default, bool jsonMode = false)
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
        public Task<JobMatch> MatchAsync(RunId runId, int index, string resumeText, JobPosting posting, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
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
