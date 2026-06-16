using JobAgents.Application.Abstractions;
using JobAgents.Web.Services;

namespace JobAgents.Api;

/// <summary>
/// Server-side equivalent of the Blazor Home page's request assembly: turns the structured
/// <see cref="SearchInputs"/> the client sends into the preferences block, run title, included
/// domains and the per-run <see cref="JobHuntConfig"/>. Keeping this on the server means the React
/// and Blazor clients run the exact same search with identical knobs.
/// </summary>
public static class HuntConfigFactory
{
    public const string AnywhereSource = "Anywhere (web)";

    /// <summary>Source label → Tavily include-domain. Empty / "Anywhere" means search the whole web.</summary>
    public static readonly IReadOnlyDictionary<string, string> SourceDomains =
        new Dictionary<string, string>
        {
            ["ITviec"] = "itviec.com",
            ["VietnamWorks"] = "vietnamworks.com",
            ["LinkedIn"] = "linkedin.com",
            ["TopCV"] = "topcv.vn",
            [AnywhereSource] = "",
        };

    /// <summary>Maps the selected source labels to Tavily include-domains (empty = whole web).</summary>
    public static IReadOnlyList<string> SelectedDomains(SearchInputs inputs)
    {
        var sources = inputs.Sources ?? [];
        if (sources.Length == 0 || sources.Contains(AnywhereSource))
            return Array.Empty<string>();

        return sources
            .Select(s => SourceDomains.GetValueOrDefault(s, string.Empty))
            .Where(d => !string.IsNullOrEmpty(d))
            .ToArray();
    }

    /// <summary>Composes the structured inputs into the preferences block the Coordinator parses.</summary>
    public static string BuildPreferences(SearchInputs i)
    {
        var lines = new List<string>();

        if (i.Roles is { Length: > 0 })
            lines.Add($"Target roles: {string.Join(", ", i.Roles)}");
        if (i.Languages is { Length: > 0 })
            lines.Add($"Languages / tech: {string.Join(", ", i.Languages)}");
        if (i.WorkingStyles is { Length: > 0 })
            lines.Add($"Working style: {string.Join(", ", i.WorkingStyles)}");
        if (!string.IsNullOrWhiteSpace(i.Location))
            lines.Add($"Location: {i.Location}");

        if (i.SalaryMin is not null || i.SalaryMax is not null)
        {
            var range = (i.SalaryMin, i.SalaryMax) switch
            {
                ({ } min, { } max) => $"{min:N0} - {max:N0}",
                ({ } min, null) => $"from {min:N0}",
                (null, { } max) => $"up to {max:N0}",
                _ => string.Empty,
            };
            lines.Add($"Expected salary: {range} {i.Currency} per year");
        }

        if (i.Sources is { Length: > 0 })
            lines.Add($"Preferred job sites: {string.Join(", ", i.Sources)}");
        if (!string.IsNullOrWhiteSpace(i.Other))
            lines.Add($"Other preferences: {i.Other.Trim()}");

        return string.Join('\n', lines);
    }

    /// <summary>A short, scannable title summarising the key search points.</summary>
    public static string BuildTitle(SearchInputs i)
    {
        var parts = new List<string>();

        if (i.Roles is { Length: > 0 })
            parts.Add(i.Roles.Length == 1 ? i.Roles[0] : $"{i.Roles[0]} +{i.Roles.Length - 1}");
        if (i.Languages is { Length: > 0 })
            parts.Add(i.Languages.Length == 1 ? i.Languages[0] : $"{i.Languages[0]} +{i.Languages.Length - 1}");
        if (i.WorkingStyles is { Length: > 0 })
            parts.Add(string.Join("/", i.WorkingStyles));
        if (!string.IsNullOrWhiteSpace(i.Location))
            parts.Add(i.Location);

        return parts.Count > 0 ? string.Join(" · ", parts) : "Job search";
    }

    /// <summary>
    /// Builds the per-run config, applying the saved per-agent model overrides and the same
    /// effort/boost escalation the Blazor "Search harder" button uses.
    /// </summary>
    public static JobHuntConfig BuildConfig(SearchInputs inputs, AgentModelConfig models, int searchBoost)
    {
        var domains = SelectedDomains(inputs);
        var maxSearches = inputs.SearchEffort + 4 * searchBoost;
        var baseResults = JobHuntConfig.Default.MaxResults + 6 * searchBoost;
        var maxResults = domains.Count > 1
            ? Math.Min(30, baseResults + 3 * (domains.Count - 1))
            : baseResults;

        return JobHuntConfig.Default with
        {
            CoordinatorModel = models.CoordinatorModel,
            SearchModel = models.SearchModel,
            ResumeMatchModel = models.ResumeMatchModel,
            CompanyResearchModel = models.CompanyResearchModel,
            SalaryAnalysisModel = models.SalaryAnalysisModel,
            InterviewPrepModel = models.InterviewPrepModel,
            IncludeDomains = domains,
            MaxResults = maxResults,
            MaxSearches = maxSearches,
            MaxFanOutConcurrency = models.ParallelSearch ? 2 : 1,
            SearchDepth = models.SearchDepth ?? SearchDepthSettings.Default,
            MaxResumeChars = models.MaxResumeChars,
            MaxDescriptionChars = models.MaxDescriptionChars,
            MaxSearchResultChars = models.MaxSearchResultChars,
            MinMatchScore = Math.Clamp(inputs.MinMatchScore, 0, 100),
            ResearchCompany = inputs.ResearchCompany,
            ResearchSalary = inputs.ResearchSalary,
            TimeRange = string.IsNullOrEmpty(inputs.PostedWithin) ? null : inputs.PostedWithin,
            StartDate = inputs.StartDate,
            EndDate = inputs.EndDate,
        };
    }
}
