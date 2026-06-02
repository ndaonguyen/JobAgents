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
public sealed class SearchAgent(IAgentRunner runner, ILogger<SearchAgent> logger) : ISearchAgent
{
    private const string SystemPrompt =
        """
        You are a job-sourcing agent. Use the Web.search_web tool to find real, currently-open job
        postings that match the candidate's criteria.

        SEARCH HARD — aim to return the full requested number of DISTINCT postings:
        - Call Web.search_web at least 4-6 times, ALWAYS passing maxResults: 10.
        - Vary each query: different role title synonyms, seniority levels, must-have skills,
          locations (city AND country), and work modes. Don't repeat the same query.
        - After your searches, if you have fewer DISTINCT postings than requested, run MORE searches
          with new angles before answering. Only stop early if extra searches clearly return nothing new.

        When the location or sites are Vietnamese (e.g. itviec.com, vietnamworks.com, topcv.vn):
        - Query in BOTH English and Vietnamese (e.g. "lập trình viên backend", "kỹ sư phần mềm",
          "tuyển dụng"), since local listings are often in Vietnamese.
        - Use country-level location terms when a city yields little (e.g. "Vietnam" as well as
          "Ho Chi Minh City" / "Hà Nội").

        Prefer recently-posted roles; skip listings that are clearly expired or stale.

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

    public async Task<IReadOnlyList<JobPosting>> FindJobsAsync(
        RunId runId, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt =
            $"""
            Find {config.MaxResults} DISTINCT job postings matching the criteria below. Aim for the
            full {config.MaxResults} — run multiple varied searches (maxResults: 10 each) to get there.
            - Roles: {Join(criteria.Roles)}
            - Locations: {Join(criteria.Locations)}
            - Seniority: {criteria.Seniority}
            - Must-have skills: {Join(criteria.MustHaveSkills)}
            - Work modes: {Join(criteria.WorkStyles)} (include roles offering ANY of these modes)
            """;

        var result = await runner.RunAsync(
            runId, AgentId.Search, "Search",
            SystemPrompt, userPrompt, config.SearchModel, useTools: true, ct);

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
