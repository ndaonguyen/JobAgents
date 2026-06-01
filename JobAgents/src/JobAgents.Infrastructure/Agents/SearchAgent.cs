using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

public interface ISearchAgent
{
    Task<IReadOnlyList<JobPosting>> FindJobsAsync(
        RunId runId, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct);
}

/// <summary>Sources live job postings by driving the Tavily web-search tool from the search criteria.</summary>
public sealed class SearchAgent(IAgentRunner runner) : ISearchAgent
{
    private const string SystemPrompt =
        """
        You are a job-sourcing agent. Use the Web.search_web tool to find real, currently-open job
        postings that match the candidate's criteria. Run SEVERAL focused searches (vary role,
        location and seniority) and call the tool multiple times before answering.

        When the location or sites are Vietnamese (e.g. itviec.com, vietnamworks.com, topcv.vn):
        - Query in BOTH English and Vietnamese (e.g. "lập trình viên backend", "kỹ sư phần mềm",
          "tuyển dụng"), since local listings are often in Vietnamese.
        - Use country-level location terms when a city yields little (e.g. "Vietnam" as well as
          "Ho Chi Minh City" / "Hà Nội").

        STRICT FILTERS — apply before returning:
        - LOCATION: only include postings located in the requested location — the city itself, its
          metro area, or its country — OR explicitly Remote when remote work is acceptable. DISCARD
          postings in a different country/region (e.g. do not return US roles for a Ho Chi Minh City
          search). Always include the target location in your search queries.
        - OPEN ONLY (when requested): only include postings that are still open and accepting
          applications. DISCARD anything showing "No longer accepting applications", "position
          closed/filled", "applications closed", or that is expired.

        Prefer recently-posted roles; skip listings that are clearly expired or stale.

        Then return ONLY a JSON array of the best DISTINCT postings (dedupe by company+title), each:
        { "title": string, "company": string, "location": string, "url": string, "summary": string,
          "postedDate": string or null }
        The "summary" is a 1-2 sentence description of the role. Set "postedDate" to the result's
        publishedDate when the tool provides one (ISO date like 2026-05-01), otherwise null — never
        guess a date. Do not invent postings or URLs; only include results grounded in the search tool
        output. Return at most the requested number.
        """;

    public async Task<IReadOnlyList<JobPosting>> FindJobsAsync(
        RunId runId, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt =
            $"""
            Find up to {config.MaxResults} job postings matching:
            - Roles: {Join(criteria.Roles)}
            - Locations: {Join(criteria.Locations)} (MUST match — discard postings elsewhere)
            - Seniority: {criteria.Seniority}
            - Must-have skills: {Join(criteria.MustHaveSkills)}
            - Remote acceptable: {criteria.RemoteOnly}
            {(config.OpenOnly ? "- Only roles still OPEN and accepting applications; discard closed/expired ones." : string.Empty)}
            """;

        var result = await runner.RunAsync(
            runId, AgentId.Search, "Search",
            SystemPrompt, userPrompt, config.SearchModel, useTools: true, ct);

        var postings = AgentJson.TryParse<List<JobPosting>>(result.Text) ?? [];

        // Safety net: drop anything that still looks closed even if the model included it.
        if (config.OpenOnly)
            postings = postings.Where(p => !LooksClosed(p)).ToList();

        return postings.Take(config.MaxResults).ToList();
    }

    private static readonly string[] ClosedMarkers =
    [
        "no longer accepting", "no longer available", "position closed", "position filled",
        "applications closed", "closed for applications", "this job has expired", "job expired",
    ];

    private static bool LooksClosed(JobPosting posting)
    {
        var text = $"{posting.Title} {posting.Summary}".ToLowerInvariant();
        return ClosedMarkers.Any(text.Contains);
    }

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(any)" : string.Join(", ", values);
}
