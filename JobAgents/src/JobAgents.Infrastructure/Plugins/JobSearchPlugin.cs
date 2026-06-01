using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobAgents.Domain.Agents;
using JobAgents.Infrastructure.Agents;
using JobAgents.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace JobAgents.Infrastructure.Plugins;

/// <summary>
/// A kernel plugin that performs live web search via the Tavily REST API. Used by the Search agent
/// to find job postings and by the Company-Research agent to look up company information. Every
/// invocation is captured onto the event bus by the kernel function filter. When the run selected
/// specific sources, the search is restricted to those domains via Tavily's <c>include_domains</c>.
/// </summary>
public sealed class JobSearchPlugin(HttpClient http, IOptions<JobAgentsOptions> options, AgentRunContext context)
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

        var request = new TavilyRequest(
            ApiKey: _tavily.ApiKey,
            Query: query,
            MaxResults: Math.Clamp(maxResults, 1, 10),
            SearchDepth: "basic",
            IncludeDomains: domains.Count > 0 ? domains.ToArray() : null,
            TimeRange: string.IsNullOrWhiteSpace(timeRange) ? null : timeRange,
            StartDate: string.IsNullOrWhiteSpace(startDate) ? null : startDate,
            EndDate: string.IsNullOrWhiteSpace(endDate) ? null : endDate);

        using var response = await http.PostAsJsonAsync(
            $"{_tavily.BaseUrl.TrimEnd('/')}/search", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return $"{{\"error\":\"Tavily request failed ({(int)response.StatusCode}): {body}\"}}";
        }

        var result = await response.Content.ReadFromJsonAsync<TavilyResponse>(JsonOptions, ct);
        var items = result?.Results ?? [];

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

    private static string Truncate(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }

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
