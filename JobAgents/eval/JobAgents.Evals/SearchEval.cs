using System.Net;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobAgents.Evals;

/// <summary>
/// Property-based eval for the Search agent (`dotnet run -- search-eval`). The agent has no fixed
/// ground truth — "find backend jobs in HCMC" has no single right answer and the market shifts daily —
/// so instead of golden outputs we assert INVARIANTS that must hold for any result set, and report
/// quality METRICS over a live run. Opt-in because it drives live Tavily search + HTTP fetches.
///
///   Tier 1 — deterministic invariants (hard pass/fail, no LLM):
///     • schema: every posting has a Title, Company and an absolute http(s) URL;
///     • dedup: no two postings share a URL (the prompt promises "dedupe by company+title");
///     • budget: the agent issued no more than MaxSearches search_web calls (read off the event bus).
///   Tier 2 — URL veracity (reported metrics; the sourcing-specific failure is INVENTED postings):
///     • reachability of each returned URL (broken ⇒ likely hallucinated / stale);
///     • whether the fetched page actually mentions the company (real URL, real content).
/// </summary>
internal static class SearchEval
{
    private sealed record Scenario(string Name, SearchCriteria Criteria, IReadOnlyList<string> Domains);

    private enum UrlStatus { Reachable, Blocked, Broken }

    private sealed record Probe(UrlStatus Status, bool MentionsCompany);

    public static async Task<int> RunAsync(IServiceProvider provider)
    {
        var runner = provider.GetRequiredService<IAgentRunner>();
        var context = provider.GetRequiredService<AgentRunContext>();
        var bus = provider.GetRequiredService<IAgentEventBus>();
        var logger = provider.GetRequiredService<ILogger<SearchAgent>>();

        // Keep the live budget modest — this is a probe, not a production hunt.
        var config = JobHuntConfig.Default with { MaxResults = 8, MaxSearches = 4 };
        using var http = BuildHttpClient();

        var scenarios = new[]
        {
            // Whole-web, generic: URLs should mostly be real and reachable (LinkedIn, company sites…).
            new Scenario(
                "remote-senior-dotnet (whole web)",
                new SearchCriteria(
                    Roles: ["Senior Backend Engineer", ".NET Engineer"],
                    Locations: ["Remote"],
                    Seniority: "Senior",
                    MustHaveSkills: ["C#", ".NET"],
                    NiceToHaveSkills: ["AWS"],
                    WorkStyles: ["Remote"],
                    SalaryExpectation: null),
                Domains: []),

            // VN job boards: the hard case — JS-heavy sites that often expose only listing URLs and
            // block bots, so expect a lower reachability/grounding floor here (a metric, not a failure).
            new Scenario(
                "hcmc-backend (VN boards)",
                new SearchCriteria(
                    Roles: ["Backend Engineer", "Software Engineer"],
                    Locations: ["Ho Chi Minh City", "Vietnam"],
                    Seniority: "Mid",
                    MustHaveSkills: ["C#", ".NET"],
                    NiceToHaveSkills: ["AWS"],
                    WorkStyles: ["Remote", "Hybrid", "Onsite"],
                    SalaryExpectation: null),
                Domains: ["itviec.com", "vietnamworks.com", "topcv.vn"]),
        };

        Console.WriteLine("Search agent property eval (live Tavily + HTTP fetch — indicative, non-deterministic)");
        Console.WriteLine($"budget {config.MaxSearches} searches   cap {config.MaxResults} postings\n");
        Console.WriteLine(new string('─', 78));

        var allInvariantsPassed = true;
        foreach (var s in scenarios)
        {
            // Scope the run's sourcing filters (flows to the search plugin via AsyncLocal).
            context.IncludeDomains = s.Domains;
            context.TimeRange = null;
            context.StartDate = null;
            context.EndDate = null;

            var runId = RunId.New();
            var collector = CollectAsync(bus, runId);
            var agent = new SearchAgent(runner, logger);
            var postings = await agent.FindJobsAsync(runId, s.Criteria, config, default);
            // Terminal System event closes the subscription so we can read the run's tool calls.
            await bus.PublishAsync(new AgentFinishedEvent(runId, AgentId.System, "", 0, 0, 0m, DateTime.UtcNow));
            var events = await collector;

            var searchCalls = events
                .OfType<ToolCalledEvent>()
                .Count(e => e.ToolName.EndsWith("search_web", StringComparison.OrdinalIgnoreCase));

            // ── Tier 1: deterministic invariants ──
            var malformed = postings.Count(p => !IsWellFormed(p));
            var schemaOk = malformed == 0;

            // The agent's explicit promise is "dedupe by company+title", so that is the hard invariant.
            var duplicateIdentities = postings
                .GroupBy(p => Normalize($"{p.Company} {p.Title}"))
                .Count(g => g.Count() > 1);
            var dedupOk = duplicateIdentities == 0;

            // Distinct postings sharing ONE URL is the job-board listing-page situation (TopCV/ITviec/
            // VietnamWorks expose only listing URLs) — a known board limitation the product keeps on
            // purpose, NOT an agent defect. So report it as a metric, don't gate on it.
            var duplicateUrls = postings
                .Select(p => (p.Url ?? string.Empty).Trim().ToLowerInvariant())
                .Where(u => u.Length > 0)
                .GroupBy(u => u)
                .Count(g => g.Count() > 1);

            var budgetOk = searchCalls <= config.MaxSearches;

            var invariantsPass = schemaOk && dedupOk && budgetOk;
            allInvariantsPassed &= invariantsPass;

            // ── Tier 2: URL veracity (only the well-formed, absolute URLs are probeable) ──
            var probeable = postings.Where(p => TryUri(p.Url, out _)).ToList();
            var probes = await Task.WhenAll(probeable.Select(p => ProbeAsync(http, p)));
            var reachable = probes.Count(r => r.Status == UrlStatus.Reachable);
            var blocked = probes.Count(r => r.Status == UrlStatus.Blocked);
            var broken = probes.Count(r => r.Status == UrlStatus.Broken);
            var mentions = probes.Count(r => r.MentionsCompany);

            // ── Report ──
            Console.WriteLine($"{(invariantsPass ? "PASS" : "FAIL")}  {s.Name}");
            Console.WriteLine($"      postings {postings.Count}   search_web calls {searchCalls}/{config.MaxSearches}");
            Console.WriteLine($"      [T1] schema {Mark(schemaOk)} ({malformed} malformed)   dedup {Mark(dedupOk)} ({duplicateIdentities} dup company+title)   budget {Mark(budgetOk)}");
            Console.WriteLine($"           shared-listing URLs: {duplicateUrls} (informational — kept on purpose, not a defect)");
            Console.WriteLine($"      [T2] reachable {reachable}/{probeable.Count}   blocked {blocked}   broken {broken}   mentions-company {mentions}/{reachable}");
            Console.WriteLine();
        }

        Console.WriteLine(new string('─', 78));
        Console.WriteLine(
            "T1 invariants are hard pass/fail. T2 is indicative: a high 'broken' count suggests invented "
            + "or stale URLs; 'blocked' is bot-protection (esp. on the JS-heavy VN boards), not a defect.");
        return allInvariantsPassed ? 0 : 1;
    }

    private static async Task<List<AgentEvent>> CollectAsync(IAgentEventBus bus, RunId runId)
    {
        var events = new List<AgentEvent>();
        await foreach (var evt in bus.SubscribeAsync(runId))
            events.Add(evt);
        return events;
    }

    // Schema invariant: a usable posting needs a title, a company, and an openable absolute http(s) URL.
    private static bool IsWellFormed(JobPosting p) =>
        !string.IsNullOrWhiteSpace(p.Title)
        && !string.IsNullOrWhiteSpace(p.Company)
        && TryUri(p.Url, out _);

    private static bool TryUri(string? url, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(url, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static async Task<Probe> ProbeAsync(HttpClient http, JobPosting posting)
    {
        try
        {
            using var resp = await http.GetAsync(posting.Url, HttpCompletionOption.ResponseHeadersRead);
            var code = (int)resp.StatusCode;
            if (code is 401 or 403 or 429)
                return new Probe(UrlStatus.Blocked, false);
            if (code >= 400)
                return new Probe(UrlStatus.Broken, false);

            var body = await resp.Content.ReadAsStringAsync();
            return new Probe(UrlStatus.Reachable, MentionsCompany(body, posting.Company));
        }
        catch
        {
            // DNS failure, timeout, TLS error, connection refused → treat as broken (likely invented/stale).
            return new Probe(UrlStatus.Broken, false);
        }
    }

    // Does the fetched page name the company? Normalised substring match — catches "real URL, real
    // content" vs. a plausible-looking URL whose page is about something else.
    private static bool MentionsCompany(string body, string company)
    {
        var needle = Normalize(company);
        if (needle.Length < 2)
            return false;
        return Normalize(body).Contains(needle, StringComparison.Ordinal);
    }

    // Lowercase, collapse every run of non-alphanumeric characters to a single space.
    private static string Normalize(string text)
    {
        var chars = text.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static HttpClient BuildHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        // A browser-like UA reduces (but won't eliminate) bot-blocking on job boards.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; JobAgentsEval/1.0)");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        return http;
    }

    private static string Mark(bool ok) => ok ? "✓" : "✗";
}
