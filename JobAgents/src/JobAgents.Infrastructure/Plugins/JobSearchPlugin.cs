using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobAgents.Domain.Agents;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace JobAgents.Infrastructure.Plugins;

/// <summary>
/// A kernel plugin that performs live web search via the Tavily REST API. Used by the Search agent
/// to find job postings and by the Company-Research agent to look up company information. Every
/// invocation is captured onto the event bus by the kernel function filter. When the run selected
/// specific sources, the search is restricted to those domains via Tavily's <c>include_domains</c>.
/// </summary>
public sealed class JobSearchPlugin(
    HttpClient http, IOptions<JobAgentsOptions> options, AgentRunContext context, ILogger<JobSearchPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TavilyOptions _tavily = options.Value.Tavily;

    [KernelFunction("search_web")]
    [Description("Searches the web for live results. Use it to find job postings or research companies.")]
    public async Task<string> SearchWebAsync(
        [Description("The search query, e.g. 'senior .NET engineer remote London'")] string query,
        [Description("Maximum number of results to return (1-10)")] int maxResults = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_tavily.ApiKey))
            return "{\"error\":\"Tavily API key is not configured.\"}";

        // Source-site and recency filters apply only to job sourcing (the Search agent). Company and
        // salary research must roam the whole, current web, so they ignore these filters.
        var isSourcing = context.CurrentAgent == AgentId.Search;
        var domains = isSourcing ? context.IncludeDomains : Array.Empty<string>();
        var startDate = isSourcing ? context.StartDate : null;
        var endDate = isSourcing ? context.EndDate : null;
        var hasExactDates = !string.IsNullOrWhiteSpace(startDate) || !string.IsNullOrWhiteSpace(endDate);
        // Exact dates take precedence over the preset window.
        var timeRange = isSourcing && !hasExactDates ? context.TimeRange : null;

        var clampedMax = Math.Clamp(maxResults, 1, 10);

        // First attempt: hard-restrict to the selected sources via Tavily's include_domains.
        var (items, error) = await SearchAsync(
            query, clampedMax, domains.Count > 0 ? domains.ToArray() : null,
            timeRange, startDate, endDate, ct);
        if (error is not null)
            return error;

        logger.LogInformation(
            "Tavily returned {Count} result(s) for query {Query} (domains: {Domains}, depth: advanced).",
            items.Count, query, Describe(domains));

        // Fallback: a hard domain restriction can shut out niche / JS-heavy sites that Tavily indexes
        // poorly (e.g. itviec.com). If it found nothing, retry across the whole web with the site names
        // folded into the query as keyword hints — biasing toward those sites without excluding others.
        if (items.Count == 0 && domains.Count > 0)
        {
            var biasedQuery = $"{query} {string.Join(' ', domains)}";
            var (fallbackItems, fallbackError) = await SearchAsync(
                biasedQuery, clampedMax, includeDomains: null, timeRange, startDate, endDate, ct);

            if (fallbackError is not null)
            {
                logger.LogWarning("Tavily fallback search failed; returning the (empty) restricted result.");
            }
            else
            {
                logger.LogInformation(
                    "Tavily domain-restricted search was empty; whole-web fallback for query {Query} returned {Count} result(s).",
                    biasedQuery, fallbackItems.Count);
                items = fallbackItems;
            }
        }

        // Return a compact JSON array the agent can reason over (incl. a published date when present).
        var projected = items.Select(r => new
        {
            r.Title,
            r.Url,
            Content = Truncate(r.Content, 600),
            PublishedDate = r.PublishedDate,
        });
        return JsonSerializer.Serialize(projected, JsonOptions);
    }

    /// <summary>
    /// Executes one Tavily search. Returns the results plus a non-null error JSON string when the
    /// request itself failed (so the caller can surface it to the agent or fall back).
    /// </summary>
    private async Task<(List<TavilyResult> Items, string? Error)> SearchAsync(
        string query, int maxResults, string[]? includeDomains,
        string? timeRange, string? startDate, string? endDate, CancellationToken ct)
    {
        // "advanced" gives much better recall on niche / JS-heavy sites (e.g. itviec.com) than "basic".
        var request = new TavilyRequest(
            ApiKey: _tavily.ApiKey,
            Query: query,
            MaxResults: maxResults,
            SearchDepth: "advanced",
            IncludeDomains: includeDomains,
            TimeRange: string.IsNullOrWhiteSpace(timeRange) ? null : timeRange,
            StartDate: string.IsNullOrWhiteSpace(startDate) ? null : startDate,
            EndDate: string.IsNullOrWhiteSpace(endDate) ? null : endDate);

        using var response = await http.PostAsJsonAsync(
            $"{_tavily.BaseUrl.TrimEnd('/')}/search", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "Tavily request failed ({Status}) for query {Query} (domains: {Domains}): {Body}",
                (int)response.StatusCode, query, includeDomains is { Length: > 0 } ? string.Join(", ", includeDomains) : "(whole web)", body);
            return ([], $"{{\"error\":\"Tavily request failed ({(int)response.StatusCode}): {body}\"}}");
        }

        var result = await response.Content.ReadFromJsonAsync<TavilyResponse>(JsonOptions, ct);
        return (result?.Results ?? [], null);
    }

    private static string Truncate(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private static string Describe(IReadOnlyList<string> domains) =>
        domains.Count > 0 ? string.Join(", ", domains) : "(whole web)";

    private sealed record TavilyRequest(
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("max_results")] int MaxResults,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("include_domains")] string[]? IncludeDomains,
        [property: JsonPropertyName("time_range")] string? TimeRange,
        [property: JsonPropertyName("start_date")] string? StartDate,
        [property: JsonPropertyName("end_date")] string? EndDate);

    private sealed record TavilyResponse(
        [property: JsonPropertyName("results")] List<TavilyResult> Results);

    private sealed record TavilyResult(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("published_date")] string? PublishedDate);
}
