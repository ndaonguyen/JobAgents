using System.Text;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Infrastructure.Sourcing;

/// <summary>
/// Identity helpers for collapsing duplicate postings. Job boards surface the same opening under many
/// URLs (tracking/query params, listing-vs-canonical, slug variants) and slightly different company
/// strings ("CodeHQ" vs "CodeHQ Vietnam"), so keying on the raw URL alone lets the same job through
/// several times. These helpers canonicalise the URL and derive a normalised title+company signature
/// so both the dedupe step and the posting cache agree on what "the same job" means.
/// </summary>
public static class PostingKey
{
    // Trailing words that don't distinguish one employer from another: legal suffixes and the
    // country/region the same company is sometimes tagged with. Stripped from the company signature
    // so "CodeHQ" and "CodeHQ Vietnam" collapse to the same key.
    private static readonly HashSet<string> CompanyNoiseWords = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "ltd", "limited", "llc", "llp", "corp", "corporation",
        "co", "company", "gmbh", "pte", "plc", "jsc", "group", "holdings",
        "vietnam", "vn", "asia", "apac", "global",
    };

    // Connector words the model bakes in when it appends the company to a title ("... at <Company>").
    // Dropped from the title signature so "<role>" and "<role> at <Company>" collapse to one job.
    private static readonly HashSet<string> TitleConnectorWords = new(StringComparer.Ordinal)
    {
        "at", "in", "for",
    };

    /// <summary>
    /// Normalises a posting URL so cosmetic variants collapse: drops the query string and fragment,
    /// strips a "www." host prefix and any trailing slash, and lower-cases the whole thing (matching
    /// the previous case-insensitive behaviour). Returns "" for a missing/blank URL.
    /// </summary>
    public static string CanonicalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var host = uri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host[4..];
            var path = uri.AbsolutePath.TrimEnd('/');
            return $"{uri.Scheme}://{host}{path}".ToLowerInvariant();
        }

        // Not a parseable http(s) URL: best-effort strip of query/fragment, then lower-case.
        var cut = trimmed.IndexOfAny(['?', '#']);
        if (cut >= 0)
            trimmed = trimmed[..cut];
        return trimmed.TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>
    /// A title+company signature that collapses the same job even when its URL differs: normalised
    /// title plus the company "core" (noise words like "Vietnam"/"Ltd" removed). The company tokens and
    /// connector words ("at"/"in"/"for") are stripped from the title too, so a posting whose title the
    /// model padded with "… at &lt;Company&gt;" folds into the bare-title duplicate. Used as the secondary
    /// dedupe key after canonical-URL collapse.
    /// </summary>
    public static string Signature(JobPosting posting)
    {
        // Strip the FULL company (incl. noise words like "Vietnam") from the title, but key on the
        // company CORE — so "… at RANGSTRUP IT Vietnam" loses every company token from its title.
        var title = StripCompanyFromTitle(NormalizeTitle(posting.Title), Normalize(posting.Company));
        return $"{title}|{NormalizeCompany(posting.Company)}";
    }

    /// <summary>Drops the full company tokens and connector words from a normalised title.</summary>
    private static string StripCompanyFromTitle(string normalizedTitle, string normalizedCompanyFull)
    {
        if (normalizedTitle.Length == 0)
            return normalizedTitle;

        var companyWords = normalizedCompanyFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var drop = new HashSet<string>(companyWords, StringComparer.Ordinal);
        drop.UnionWith(TitleConnectorWords);

        var kept = normalizedTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !drop.Contains(word))
            .ToArray();

        // If stripping emptied the title (e.g. title was literally the company), keep the original.
        return kept.Length > 0 ? string.Join(' ', kept) : normalizedTitle;
    }

    /// <summary>Lower-cases and collapses every run of non-alphanumeric characters to a single space.</summary>
    public static string NormalizeTitle(string? title) => Normalize(title);

    /// <summary>Normalises the company name and drops trailing legal/region noise words.</summary>
    public static string NormalizeCompany(string? company)
    {
        var normalized = Normalize(company);
        if (normalized.Length == 0)
            return string.Empty;

        var core = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !CompanyNoiseWords.Contains(word))
            .ToArray();

        // If stripping noise words emptied it (e.g. company was literally "Ltd"), keep the full name.
        return core.Length > 0 ? string.Join(' ', core) : normalized;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return sb.ToString();
    }
}
