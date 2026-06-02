using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace JobAgents.Infrastructure.Agents;

public interface ISearchAgent
{
    Task<IReadOnlyList<JobPosting>> FindJobsAsync(
        RunId runId, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct);
}

/// <summary>Sources live job postings by driving the Tavily web-search tool from the search criteria.</summary>
public sealed class SearchAgent(IAgentRunner runner, ILogger<SearchAgent> logger, string? systemPrompt = null)
    : ISearchAgent
{
    // The system prompt is composed from a fixed head/tail plus a swappable URL-quality block, so an
    // eval can A/B alternative wordings of just that block without forking the whole prompt.
    private const string PromptHead =
        """
        You are a job-sourcing agent. Use the Web.search_web tool to find real, currently-open job
        postings that match the candidate's criteria.

        SEARCH STRATEGY — maximise DISTINCT postings within your search budget:
        - The user prompt gives you a search BUDGET (a maximum number of searches). Use up to that
          many Web.search_web calls — no more — ALWAYS passing maxResults: 10.
        - Vary each query: different role title synonyms, seniority levels, must-have skills,
          locations (city AND country), and work modes. Don't repeat the same query.
        - Spend the budget wisely on varied angles; stop once you hit the budget or extra searches
          clearly return nothing new.

        When the location or sites are Vietnamese (e.g. itviec.com, vietnamworks.com, topcv.vn):
        - Query in BOTH English and Vietnamese (e.g. "lập trình viên backend", "kỹ sư phần mềm",
          "tuyển dụng"), since local listings are often in Vietnamese.
        - Use country-level location terms when a city yields little (e.g. "Vietnam" as well as
          "Ho Chi Minh City" / "Hà Nội").

        Prefer recently-posted roles; skip listings that are clearly expired or stale.
        """;

    /// <summary>The current production wording of the URL-quality guidance (a preference + a "don't drop" guard).</summary>
    public const string DefaultUrlQualityBlock =
        """
        URL QUALITY: PREFER a "url" that points to ONE specific job's own detail page (a page a user can
        open to read and apply to that single role) over a job-board search/listing/category page that
        enumerates many jobs (e.g. TopCV "tim-viec-lam-…"/"…-kl<number>", ITviec "/it-jobs",
        VietnamWorks "/viec-lam" search pages). When you have a role's individual detail URL, always use
        it. BUT do NOT drop a real, relevant posting just because the only URL available is a listing
        page — these boards (TopCV/ITviec/VietnamWorks) are JS-heavy and often only expose listing URLs,
        and the UI labels such links so the user isn't misled. Finding the role is more important than a
        perfect URL.
        """;

    private const string PromptTail =
        """
        Then return ONLY a JSON array of the best DISTINCT postings (dedupe by company+title), each:
        { "title": string, "company": string, "location": string, "url": string, "summary": string,
          "postedDate": string or null, "description": string }
        The "summary" is a 1-2 sentence description of the role. The "description" is the FULL posting
        detail you found — responsibilities, requirements, required skills/tech, seniority and any
        salary — quoted or closely paraphrased from the search result content (not invented). Include
        as much concrete requirement text as the tool returned; this is what the resume-matcher scores
        against, so do not summarise it away. Set "postedDate" to the result's publishedDate when the
        tool provides one (ISO date like 2026-05-01), otherwise null — never guess a date. Do not
        invent postings, URLs, or requirements; only include details grounded in the search tool
        output. Return at most the requested number.
        """;

    /// <summary>Composes the full system prompt around a given URL-quality block.</summary>
    public static string BuildSystemPrompt(string urlQualityBlock) =>
        $"{PromptHead}\n\n{urlQualityBlock}\n\n{PromptTail}";

    /// <summary>The default production system prompt.</summary>
    public static readonly string DefaultSystemPrompt = BuildSystemPrompt(DefaultUrlQualityBlock);

    private readonly string _systemPrompt = systemPrompt ?? DefaultSystemPrompt;

    public async Task<IReadOnlyList<JobPosting>> FindJobsAsync(
        RunId runId, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt =
            $"""
            Find up to {config.MaxResults} DISTINCT job postings matching the criteria below.
            SEARCH BUDGET: run AT MOST {config.MaxSearches} searches (maxResults: 10 each), each a
            different angle. Stop after {config.MaxSearches} searches and return what you found.
            - Roles: {Join(criteria.Roles)}
            - Locations: {Join(criteria.Locations)}
            - Seniority: {criteria.Seniority}
            - Must-have skills: {Join(criteria.MustHaveSkills)}
            - Work modes: {Join(criteria.WorkStyles)} (include roles offering ANY of these modes)
            """;

        var result = await runner.RunAsync(
            runId, AgentId.Search, "Search",
            _systemPrompt, userPrompt, config.SearchModel, useTools: true, ct);

        var postings = AgentJson.TryParse<List<JobPosting>>(result.Text) ?? [];
        if (postings.Count == 0)
        {
            logger.LogWarning(
                "Search agent produced no postings (domains: {Domains}). Raw model output: {Output}",
                config.IncludeDomains is { Count: > 0 } d ? string.Join(", ", d) : "(whole web)",
                Truncate(result.Text, 2000));
        }

        return postings.Take(config.MaxResults).ToList();
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(any)" : string.Join(", ", values);

    private static string Truncate(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
