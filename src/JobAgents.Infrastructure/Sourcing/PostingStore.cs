using System.Text.Json;
using JobAgents.Application.Abstractions;
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
    /// <paramref name="postedWithin"/>, and match <paramref name="criteria"/>. When an embedding provider
    /// is configured, matching + ranking is semantic (cosine over per-posting vectors) with a keyword
    /// fallback; otherwise it is keyword-only and freshest-first. Up to <paramref name="max"/> results.
    /// These are candidate postings; the resume matcher still scores them.
    /// </summary>
    Task<IReadOnlyList<JobPosting>> QueryAsync(SearchCriteria criteria, string? postedWithin, int max, CancellationToken ct = default);

    /// <summary>Upserts newly-found postings (by URL), refreshing their cached-at timestamp.</summary>
    Task SaveAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default);
}

/// <summary>No-op store (default): never serves cached postings, never persists. Used outside the web app.</summary>
public sealed class NullPostingStore : IPostingStore
{
    public Task<IReadOnlyList<JobPosting>> QueryAsync(SearchCriteria criteria, string? postedWithin, int max, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JobPosting>>([]);

    public Task SaveAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>One cached posting, when we fetched it (TTL anchor / "still open" proxy), and its embedding
/// vector (null until embedded — e.g. saved with no provider configured).</summary>
internal sealed record StoredPosting(JobPosting Posting, DateTime FetchedAtUtc, float[]? Embedding = null);

/// <summary>
/// File-backed <see cref="IPostingStore"/> — a single JSON file in the results directory, loaded into
/// memory and rewritten on save (upsert by URL, expired/over-cap rows pruned). Simple storage, no DB.
/// </summary>
public sealed class FilePostingStore : IPostingStore
{
    // How long a cached copy stays reusable. Jobs close, so this is a "probably still open" proxy,
    // not a guarantee.
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(14);
    private const int MaxEntries = 3000;

    // Cosine floor for a semantic topic match. text-embedding-3-small similarities for genuinely
    // related roles sit comfortably above this; unrelated postings fall below.
    private const double SemanticFloor = 0.35;

    // Generic role words that don't help narrow a search; stripped before keyword matching.
    private static readonly HashSet<string> GenericRoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "senior", "junior", "mid", "middle", "lead", "staff", "principal", "engineer", "developer",
        "software", "sr", "jr", "i", "ii", "iii",
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly IEmbeddingService? _embeddings;
    private readonly object _gate = new();
    private readonly Dictionary<string, StoredPosting> _cache = new();
    private bool _loaded;

    /// <param name="embeddings">
    /// Optional embedding provider. When null or disabled, retrieval is keyword-only (freshest-first) —
    /// identical to the pre-vector behaviour.
    /// </param>
    public FilePostingStore(string directory, IEmbeddingService? embeddings = null)
    {
        _path = Path.Combine(directory, "posting-cache.json");
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<JobPosting>> QueryAsync(
        SearchCriteria criteria, string? postedWithin, int max, CancellationToken ct = default)
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

        // Hard-gate candidates (freshness, posted-window, seniority, location) under the lock — these
        // never depend on embeddings. Topic relevance is decided after, semantically or by keyword.
        List<StoredPosting> candidates;
        lock (_gate)
        {
            EnsureLoaded();
            candidates = _cache.Values
                .Where(e => now - e.FetchedAtUtc <= Ttl)               // our copy still fresh
                .Where(e => WithinWindow(e, window, now))              // posted within the user's range
                .Where(e => !Seniority.IsBelowFloor(e.Posting, seniorityFloor))
                .Where(e => LocationOk(e.Posting, locTokens, remoteOk))
                .ToList();
        }

        if (candidates.Count == 0)
            return [];

        // Embed the criteria once; null when no provider, the request failed, or nothing came back —
        // in which case we fall back to keyword topic matching only.
        var queryVec = await EmbedQueryAsync(criteria, ct);

        var scored = new List<(StoredPosting Entry, double Score)>();
        foreach (var e in candidates)
        {
            var cosine = (queryVec is not null && e.Embedding is { Length: > 0 })
                ? Cosine(queryVec, e.Embedding)
                : double.NaN;
            var semanticMatch = !double.IsNaN(cosine) && cosine >= SemanticFloor;
            var keywordMatch = TopicMatch(e.Posting, roleTokens, skillTokens);

            // Topic gate: either the keyword tokens hit, or the posting is semantically close. Semantic
            // recall catches synonyms keyword matching misses ("ML engineer" ↔ "AI engineer").
            if (semanticMatch || keywordMatch)
                scored.Add((e, double.IsNaN(cosine) ? 0d : cosine));
        }

        return scored
            .OrderByDescending(s => s.Score)                  // semantic relevance first (0 when no vector)
            .ThenByDescending(s => s.Entry.FetchedAtUtc)      // then freshest — and the sole order in keyword mode
            .Take(max)
            .Select(s => s.Entry.Posting)
            .ToList();
    }

    public async Task SaveAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default)
    {
        var incoming = postings.Where(p => Key(p).Length > 0).ToList();
        if (incoming.Count == 0)
            return;

        // Embed only postings we don't already hold a vector for (new, or previously embedded with no
        // provider). One batched request keeps cost/latency low; failure ⇒ stored with a null vector.
        List<JobPosting> toEmbed;
        lock (_gate)
        {
            EnsureLoaded();
            toEmbed = incoming
                .Where(p => _cache.GetValueOrDefault(Key(p))?.Embedding is not { Length: > 0 })
                .ToList();
        }

        var vectors = new Dictionary<string, float[]>();
        if (_embeddings?.IsEnabled == true && toEmbed.Count > 0)
        {
            var embedded = await _embeddings.EmbedAsync(toEmbed.Select(EmbedText).ToList(), ct);
            for (var i = 0; i < embedded.Count && i < toEmbed.Count; i++)
                if (embedded[i] is { Length: > 0 } vec)
                    vectors[Key(toEmbed[i])] = vec;
        }

        lock (_gate)
        {
            EnsureLoaded();
            var now = DateTime.UtcNow;
            foreach (var posting in incoming)
            {
                var key = Key(posting);
                // Keep an existing vector if we didn't just compute a fresh one for this key.
                var embedding = vectors.GetValueOrDefault(key) ?? _cache.GetValueOrDefault(key)?.Embedding;
                _cache[key] = new StoredPosting(posting, now, embedding);
            }

            Prune(now);
            Persist();
        }
    }

    private async Task<float[]?> EmbedQueryAsync(SearchCriteria criteria, CancellationToken ct)
    {
        if (_embeddings?.IsEnabled != true)
            return null;

        var vecs = await _embeddings.EmbedAsync([QueryText(criteria)], ct);
        return vecs.Count > 0 && vecs[0] is { Length: > 0 } v ? v : null;
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

    // Keyword topic relevance: role or skill tokens appear in the posting text (level-agnostic). When the
    // criteria carry no role/skill tokens, every posting is topically in-scope.
    private static bool TopicMatch(JobPosting posting, HashSet<string> roleTokens, List<string> skillTokens)
    {
        if (roleTokens.Count == 0 && skillTokens.Count == 0)
            return true;

        var text = $"{posting.Title} {posting.Summary} {posting.Description} {posting.Location}".ToLowerInvariant();
        return roleTokens.Any(text.Contains) || skillTokens.Any(text.Contains);
    }

    // Hard location gate (applies in both keyword and semantic modes): the posting must be in one of the
    // requested locations, or remote when the user accepts remote.
    private static bool LocationOk(JobPosting posting, List<string> locTokens, bool remoteOk)
    {
        if (locTokens.Count == 0)
            return true;

        var text = $"{posting.Title} {posting.Summary} {posting.Description} {posting.Location}".ToLowerInvariant();
        return locTokens.Any(text.Contains) || (remoteOk && text.Contains("remote"));
    }

    // Text embedded per posting: the fields that carry topic signal, capped so a long description can't
    // blow up token cost. Title/summary lead so they dominate the vector.
    private static string EmbedText(JobPosting posting)
    {
        var text = $"{posting.Title}. {posting.Summary}. {posting.Location}. {posting.Description}";
        return text.Length > 2000 ? text[..2000] : text;
    }

    // The search criteria rendered as a single query string to embed against the posting vectors.
    private static string QueryText(SearchCriteria criteria)
    {
        var roles = string.Join(", ", criteria.Roles);
        var skills = string.Join(", ", criteria.MustHaveSkills.Concat(criteria.NiceToHaveSkills));
        var locations = string.Join(", ", criteria.Locations);
        return $"{criteria.Seniority} {roles} roles. Skills: {skills}. Location: {locations}.";
    }

    private static double Cosine(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA <= 0 || normB <= 0 ? 0d : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
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
