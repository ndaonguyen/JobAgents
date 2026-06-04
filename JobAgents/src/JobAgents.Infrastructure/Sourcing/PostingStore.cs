using System.Text.Json;
using JobAgents.Domain.JobHunt;

namespace JobAgents.Infrastructure.Sourcing;

/// <summary>
/// A corpus of postings the Search agent has already fetched, so a new search can REUSE fresh,
/// criteria-matching rows instead of re-querying Tavily ("retrieve-before-fetch"). Cuts Tavily
/// requests and grows the available result pool across runs.
/// </summary>
public interface IPostingStore
{
    /// <summary>
    /// Returns cached postings that are still fresh (within TTL), were posted within
    /// <paramref name="postedWithin"/>, and match <paramref name="criteria"/> — freshest first, up to
    /// <paramref name="max"/>. These are candidate postings; the resume matcher still scores them.
    /// </summary>
    IReadOnlyList<JobPosting> Query(SearchCriteria criteria, string? postedWithin, int max);

    /// <summary>Upserts newly-found postings (by URL), refreshing their cached-at timestamp.</summary>
    Task SaveAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default);
}

/// <summary>No-op store (default): never serves cached postings, never persists. Used outside the web app.</summary>
public sealed class NullPostingStore : IPostingStore
{
    public IReadOnlyList<JobPosting> Query(SearchCriteria criteria, string? postedWithin, int max) => [];

    public Task SaveAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>One cached posting plus when we fetched it (the TTL anchor / "still open" proxy).</summary>
internal sealed record StoredPosting(JobPosting Posting, DateTime FetchedAtUtc);

/// <summary>
/// File-backed <see cref="IPostingStore"/> — a single JSON file in the results directory, loaded into
/// memory and rewritten on save (upsert by URL, expired/over-cap rows pruned). Simple storage, no DB.
/// </summary>
public sealed class FilePostingStore : IPostingStore
{
    // How long a cached copy stays reusable. Short, because jobs close — this is a "probably still
    // open" proxy, not a guarantee.
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);
    private const int MaxEntries = 3000;

    // Generic role words that don't help narrow a search; stripped before keyword matching.
    private static readonly HashSet<string> GenericRoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "senior", "junior", "mid", "middle", "lead", "staff", "principal", "engineer", "developer",
        "software", "sr", "jr", "i", "ii", "iii",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredPosting> _cache = new();
    private bool _loaded;

    public FilePostingStore(string directory) =>
        _path = Path.Combine(directory, "posting-cache.json");

    public IReadOnlyList<JobPosting> Query(SearchCriteria criteria, string? postedWithin, int max)
    {
        if (max <= 0)
            return [];

        var window = Window(postedWithin);
        var now = DateTime.UtcNow;

        var roleTokens = criteria.Roles
            .SelectMany(SplitWords)
            .Where(w => w.Length > 2 && !GenericRoleWords.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
        var skillTokens = criteria.MustHaveSkills.Concat(criteria.NiceToHaveSkills)
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .ToList();
        var locTokens = criteria.Locations
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => l.Length > 0)
            .ToList();
        var remoteOk = criteria.WorkStyles.Any(w => w.Contains("remote", StringComparison.OrdinalIgnoreCase));
        // Seniority floor: GenericRoleWords above strips "senior/lead/staff/…" so role matching stays
        // level-agnostic, but a cached posting clearly BELOW the requested level must not be served back.
        var seniorityFloor = Seniority.Parse(criteria.Seniority);

        lock (_gate)
        {
            EnsureLoaded();
            return _cache.Values
                .Where(e => now - e.FetchedAtUtc <= Ttl)               // our copy still fresh
                .Where(e => WithinWindow(e, window, now))              // posted within the user's range
                .Where(e => !Seniority.IsBelowFloor(e.Posting, seniorityFloor))
                .Where(e => Fits(e.Posting, roleTokens, skillTokens, locTokens, remoteOk))
                .OrderByDescending(e => e.FetchedAtUtc)
                .Take(max)
                .Select(e => e.Posting)
                .ToList();
        }
    }

    public Task SaveAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var now = DateTime.UtcNow;
            foreach (var posting in postings)
            {
                var key = Key(posting);
                if (key.Length > 0)
                    _cache[key] = new StoredPosting(posting, now);
            }

            Prune(now);
            Persist();
        }

        return Task.CompletedTask;
    }

    // ── internals ─────────────────────────────────────────────────────────────────────────────────

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        try
        {
            if (!File.Exists(_path))
                return;

            var stored = JsonSerializer.Deserialize<List<StoredPosting>>(File.ReadAllText(_path), JsonOptions) ?? [];
            var now = DateTime.UtcNow;
            foreach (var entry in stored)
                if (now - entry.FetchedAtUtc <= Ttl)
                {
                    var key = Key(entry.Posting);
                    if (key.Length > 0)
                        _cache[key] = entry;
                }
        }
        catch
        {
            // Corrupt/unreadable cache is non-fatal: start empty rather than break the run.
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var key in _cache.Where(kv => now - kv.Value.FetchedAtUtc > Ttl).Select(kv => kv.Key).ToList())
            _cache.Remove(key);

        if (_cache.Count <= MaxEntries)
            return;

        // Over cap: keep the most-recently-fetched entries.
        foreach (var key in _cache
            .OrderBy(kv => kv.Value.FetchedAtUtc)
            .Take(_cache.Count - MaxEntries)
            .Select(kv => kv.Key)
            .ToList())
            _cache.Remove(key);
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_cache.Values, JsonOptions));
        }
        catch
        {
            // A failed cache write must not break a run.
        }
    }

    private static bool WithinWindow(StoredPosting entry, TimeSpan? window, DateTime now)
    {
        if (window is null)
            return true; // "any time"

        // Prefer the job's posted date; fall back to when we fetched it if the date is missing/unparseable.
        var effective = DateTime.TryParse(entry.Posting.PostedDate, out var posted) ? posted : entry.FetchedAtUtc;
        return now - effective <= window.Value;
    }

    private static bool Fits(
        JobPosting posting, HashSet<string> roleTokens, List<string> skillTokens, List<string> locTokens, bool remoteOk)
    {
        var text = $"{posting.Title} {posting.Summary} {posting.Description} {posting.Location}".ToLowerInvariant();

        var topicMatch = (roleTokens.Count == 0 && skillTokens.Count == 0)
            || roleTokens.Any(text.Contains)
            || skillTokens.Any(text.Contains);
        if (!topicMatch)
            return false;

        return locTokens.Count == 0
            || locTokens.Any(text.Contains)
            || (remoteOk && text.Contains("remote"));
    }

    private static TimeSpan? Window(string? postedWithin) => postedWithin?.Trim().ToLowerInvariant() switch
    {
        "day" => TimeSpan.FromDays(1),
        "week" => TimeSpan.FromDays(7),
        "month" => TimeSpan.FromDays(31),
        "year" => TimeSpan.FromDays(366),
        _ => null, // empty / "any" → no recency bound
    };

    private static IEnumerable<string> SplitWords(string value) =>
        value.Split([' ', '\t', '-', '/', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant());

    // Key on the CANONICAL url (query/fragment/slug variants folded together) so the corpus stops
    // hoarding several copies of one listing across runs; fall back to the title+company signature when
    // there's no url. Shared with Coordinator.Dedupe via PostingKey so both agree on posting identity.
    private static string Key(JobPosting posting)
    {
        var url = PostingKey.CanonicalUrl(posting.Url);
        return url.Length > 0 ? url : PostingKey.Signature(posting);
    }
}
