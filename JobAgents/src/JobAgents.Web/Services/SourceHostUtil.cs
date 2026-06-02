using System.Text.RegularExpressions;

namespace JobAgents.Web.Services;

/// <summary>Derives a short, friendly source host (e.g. "itviec.com") from a posting URL.</summary>
public static partial class SourceHostUtil
{
    public static string From(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "other";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "other";

        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// Heuristic: does this URL point at a job-board SEARCH / LISTING / CATEGORY page (many jobs)
    /// rather than a single posting? Tavily often indexes these for JS-heavy VN boards, and the link
    /// then "shows nothing" or a changing list. Deliberately conservative — better to under-flag than
    /// to mislabel a real posting. Used only to relabel the link, never to hide it.
    /// </summary>
    public static bool IsListing(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.ToLowerInvariant();
        var query = uri.Query.ToLowerInvariant();

        // Path signals: TopCV "tim-viec-lam-…", a search path, or ITviec's job-list section.
        if (path.Contains("tim-viec-lam") || path.Contains("/tim-kiem") || path.Contains("/search") || path.Contains("/it-jobs"))
            return true;

        // TopCV listing slug suffix, e.g. "…-kl2" / "…-kl15".
        if (ListingSuffix().IsMatch(path))
            return true;

        // Search-style query params (keyword/page) on a results page.
        return query.Contains("q=") || query.Contains("keyword") || query.Contains("search") || query.Contains("page=");
    }

    [GeneratedRegex(@"-kl\d+/?$")]
    private static partial Regex ListingSuffix();
}
