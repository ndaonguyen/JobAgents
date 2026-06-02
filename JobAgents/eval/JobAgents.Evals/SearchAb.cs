using System.Text.RegularExpressions;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using JobAgents.Infrastructure.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobAgents.Evals;

/// <summary>
/// One-off A/B harness for the Search agent's URL-quality wording. Runs the SAME criteria through two
/// system-prompt variants (the current "PREFER detail URLs…" wording vs. a "recall-first" rewrite) and
/// reports how many distinct postings each returns and how the URLs split detail-vs-listing — to test
/// whether the "PREFER…" phrasing quietly suppresses postings whose only URL is a listing page.
/// Tavily responses are cached process-wide, so the second variant reuses the first's raw search rows
/// where queries match: same input data, different prompt → the count delta is the prompt's effect.
/// </summary>
internal static partial class SearchAb
{
    // Recall-first rewrite of SearchAgent.DefaultUrlQualityBlock: leads with "include everything",
    // demotes the URL preference to a strictly-secondary, post-inclusion concern.
    private const string RecallFirstUrlBlock =
        """
        URL QUALITY (strictly secondary to recall): Include EVERY real, relevant posting you find.
        NEVER drop, skip, or omit a posting because its only available URL is a job-board
        search/listing/category page — these boards (TopCV/ITviec/VietnamWorks) are JS-heavy and
        frequently expose only listing URLs, and the UI labels such links so the user isn't misled.
        Finding the role always wins. ONLY after you have decided to include a posting, prefer a "url"
        that points to that ONE job's own detail page over a listing page (e.g. TopCV
        "tim-viec-lam-…"/"…-kl<number>", ITviec "/it-jobs", VietnamWorks "/viec-lam"); when a role's
        individual detail URL is available, use it.
        """;

    public static async Task<int> RunAsync(IServiceProvider provider)
    {
        var runner = provider.GetRequiredService<IAgentRunner>();
        var context = provider.GetRequiredService<AgentRunContext>();
        var logger = provider.GetRequiredService<ILogger<SearchAgent>>();

        var criteria = new SearchCriteria(
            Roles: ["Backend Engineer", "Software Engineer"],
            Locations: ["Ho Chi Minh City", "Vietnam"],
            Seniority: "Mid",
            MustHaveSkills: ["C#", ".NET"],
            NiceToHaveSkills: ["AWS"],
            WorkStyles: ["Remote", "Hybrid", "Onsite"],
            SalaryExpectation: null);

        // Target the VN boards, where the listing-vs-detail URL tension actually shows up.
        context.IncludeDomains = ["itviec.com", "vietnamworks.com", "topcv.vn"];

        var config = JobHuntConfig.Default;

        var variants = new (string Name, string Prompt)[]
        {
            ("current (prefer-first)", SearchAgent.DefaultSystemPrompt),
            ("recall-first (rewrite)", SearchAgent.BuildSystemPrompt(RecallFirstUrlBlock)),
        };

        Console.WriteLine(
            $"Search URL-wording A/B  (1 trial each — indicative only; LLM + search are non-deterministic)");
        Console.WriteLine(
            $"domains: {string.Join(", ", context.IncludeDomains)}   budget {config.MaxSearches} searches   cap {config.MaxResults}");
        Console.WriteLine($"roles: {string.Join(", ", criteria.Roles)}\n");
        Console.WriteLine(new string('─', 78));

        foreach (var (name, prompt) in variants)
        {
            var agent = new SearchAgent(runner, logger, prompt);
            var postings = await agent.FindJobsAsync(RunId.New(), criteria, config, default);

            var total = postings.Count;
            var distinct = postings
                .Select(p => $"{p.Company}{p.Title}".ToLowerInvariant())
                .Distinct().Count();
            var listing = postings.Count(p => IsListing(p.Url));

            Console.WriteLine($"{name}");
            Console.WriteLine($"      postings returned  : {total}");
            Console.WriteLine($"      distinct (co+title): {distinct}");
            Console.WriteLine($"      detail URLs        : {total - listing}");
            Console.WriteLine($"      listing URLs       : {listing}");
            foreach (var p in postings)
                Console.WriteLine($"        {(IsListing(p.Url) ? "L" : "D")}  {p.Company} — {p.Title}  ({p.Url})");
            Console.WriteLine();
        }

        Console.WriteLine(new string('─', 78));
        Console.WriteLine(
            "Read: if 'recall-first' returns meaningfully MORE postings (esp. listing-URL ones), the " +
            "current 'PREFER…' wording is suppressing results. Similar counts → wording is count-neutral.");
        return 0;
    }

    // Mirrors JobAgents.Web SourceHostUtil.IsListing (not referenced by this project) — conservative
    // detection of job-board search/listing pages so we can split the returned URLs detail-vs-listing.
    private static bool IsListing(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.ToLowerInvariant();
        var query = uri.Query.ToLowerInvariant();

        if (path.Contains("tim-viec-lam") || path.Contains("/tim-kiem") || path.Contains("/search") || path.Contains("/it-jobs"))
            return true;
        if (ListingSuffix().IsMatch(path))
            return true;
        return query.Contains("q=") || query.Contains("keyword") || query.Contains("search") || query.Contains("page=");
    }

    [GeneratedRegex(@"-kl\d+/?$")]
    private static partial Regex ListingSuffix();
}
