using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly JoobleOptions _jooble = options.Value.Jooble;

    [KernelFunction("search_web")]
    [Description("Searches the web for live results. Use it to find job postings or research companies.")]
    public async Task<string> SearchWebAsync(
        [Description("The search query, e.g. 'senior .NET engineer remote London'")] string query,
        [Description("Maximum number of results to return (1-10)")] int maxResults = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_tavily.ApiKey))
            return "{\"error\":\"Tavily API key is not configured.\"}";

        var domains = context.IncludeDomains;
        var request = new TavilyRequest(
            ApiKey: _tavily.ApiKey,
            Query: query,
            MaxResults: Math.Clamp(maxResults, 1, 10),
            SearchDepth: "basic",
            IncludeDomains: domains.Count > 0 ? domains.ToArray() : null);

        using var response = await http.PostAsJsonAsync(
            $"{_tavily.BaseUrl.TrimEnd('/')}/search", request, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return $"{{\"error\":\"Tavily request failed ({(int)response.StatusCode}): {body}\"}}";
        }

        var result = await response.Content.ReadFromJsonAsync<TavilyResponse>(JsonOptions, ct);
        var items = result?.Results ?? [];

        // Return a compact JSON array the agent can reason over.
        var projected = items.Select(r => new { r.Title, r.Url, Content = Truncate(r.Content, 600) });
        return JsonSerializer.Serialize(projected, JsonOptions);
    }

    [KernelFunction("search_job_board")]
    [Description("Searches a structured job board (Jooble) for postings, returning title, company, " +
                 "location, salary and link. Prefer this for concrete listings; if it reports it is " +
                 "unavailable, rely on search_web instead.")]
    public async Task<string> SearchJobBoardAsync(
        [Description("Job keywords, e.g. 'senior backend C#'")] string keywords,
        [Description("Location, e.g. 'Ho Chi Minh City' or 'Remote' (optional)")] string location = "",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_jooble.ApiKey))
            return "{\"unavailable\":\"Job board (Jooble) is not configured.\"}";

        var url = $"{_jooble.BaseUrl.TrimEnd('/')}/{_jooble.ApiKey}";
        var request = new JoobleRequest(keywords, location);

        using var response = await http.PostAsJsonAsync(url, request, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return $"{{\"error\":\"Jooble request failed ({(int)response.StatusCode}): {body}\"}}";
        }

        var result = await response.Content.ReadFromJsonAsync<JoobleResponse>(JsonOptions, ct);
        var jobs = result?.Jobs ?? [];
        var projected = jobs
            .Take(10)
            .Select(j => new { j.Title, j.Company, j.Location, j.Salary, Url = j.Link, Snippet = Truncate(j.Snippet, 400) });
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
        [property: JsonPropertyName("include_domains")] string[]? IncludeDomains);

    private sealed record TavilyResponse(
        [property: JsonPropertyName("results")] List<TavilyResult> Results);

    private sealed record TavilyResult(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content);

    private sealed record JoobleRequest(
        [property: JsonPropertyName("keywords")] string Keywords,
        [property: JsonPropertyName("location")] string Location);

    private sealed record JoobleResponse(
        [property: JsonPropertyName("jobs")] List<JoobleJob> Jobs);

    private sealed record JoobleJob(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("company")] string? Company,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("salary")] string? Salary,
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("snippet")] string? Snippet);
}
