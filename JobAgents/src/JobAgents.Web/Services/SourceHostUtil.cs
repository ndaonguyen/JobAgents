namespace JobAgents.Web.Services;

/// <summary>Derives a short, friendly source host (e.g. "itviec.com") from a posting URL.</summary>
public static class SourceHostUtil
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
}
