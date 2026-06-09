using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Plugins;

/// <summary>One Tavily search result row (title/url/content/date). Shared between the plugin and cache.</summary>
public sealed record TavilyResult(
    string? Title,
    string? Url,
    string? Content,
    [property: JsonPropertyName("published_date")] string? PublishedDate);

/// <summary>
/// Caches Tavily responses plus a per-run whole-web fallback budget, to cut the number of real HTTP
/// requests the agents send. Responses are keyed by the full request signature and expire after a TTL,
/// so identical queries — within a run, across "Search harder" re-runs, and across separate runs — reuse
/// one response instead of re-querying Tavily. A cache hit replays the exact same rows, so results (and
/// therefore match quality) are unchanged; only the paid HTTP round-trip is skipped.
///
/// When a cache directory is supplied, responses are ALSO written to disk so they survive an app restart
/// and the in-memory eviction, within the same TTL. Registered as a singleton.
/// </summary>
public sealed class TavilySearchCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);
    private const int MaxEntries = 512;
    // Generous per-run guard: the whole-web fallback is how VN sites get found, so we only cap runaway
    // doubling rather than starve recall. Distinct repeats are already absorbed by the response cache.
    private const int MaxFallbacksPerRun = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<RunId, int> _fallbacks = new();
    // Null disables disk persistence (memory-only) — used by evals/tests; the web app supplies a path.
    private readonly string? _diskDir;

    public TavilySearchCache(string? cacheDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(cacheDirectory))
        {
            try
            {
                Directory.CreateDirectory(cacheDirectory);
                _diskDir = cacheDirectory;
            }
            catch
            {
                _diskDir = null; // Unwritable path → silently fall back to memory-only.
            }
        }
    }

    public bool TryGet(string key, out IReadOnlyList<TavilyResult> items)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > DateTime.UtcNow)
        {
            items = entry.Items;
            return true;
        }

        // Memory miss (cold start, evicted, or expired in RAM) → try disk before giving up.
        return TryLoadFromDisk(key, out items);
    }

    public void Set(string key, IReadOnlyList<TavilyResult> items)
    {
        if (_entries.Count >= MaxEntries)
            Prune();

        var expires = DateTime.UtcNow.Add(Ttl);
        _entries[key] = new Entry(expires, items);
        WriteToDisk(key, expires, items);
    }

    /// <summary>True while this run is still within its whole-web fallback budget.</summary>
    public bool TryUseFallback(RunId runId) =>
        _fallbacks.AddOrUpdate(runId, 1, static (_, n) => n + 1) <= MaxFallbacksPerRun;

    private bool TryLoadFromDisk(string key, out IReadOnlyList<TavilyResult> items)
    {
        items = Array.Empty<TavilyResult>();
        if (_diskDir is null)
            return false;

        var path = PathFor(key);
        try
        {
            if (!File.Exists(path))
                return false;

            var entry = JsonSerializer.Deserialize<DiskEntry>(File.ReadAllText(path), JsonOptions);
            if (entry is null || entry.Expires <= DateTime.UtcNow)
            {
                TryDelete(path); // Expired/garbage → drop it.
                return false;
            }

            items = entry.Items;
            // Warm the in-memory cache (keep the disk file's original expiry) for the rest of the process.
            _entries[key] = new Entry(entry.Expires, items);
            return true;
        }
        catch
        {
            return false; // Unreadable/partial file → treat as a miss.
        }
    }

    private void WriteToDisk(string key, DateTime expires, IReadOnlyList<TavilyResult> items)
    {
        if (_diskDir is null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(new DiskEntry(expires, items), JsonOptions);
            File.WriteAllText(PathFor(key), json);
        }
        catch
        {
            // Persistence is best-effort: a failed write just means the next identical query re-fetches.
        }
    }

    // SHA-256 of the request signature → a safe, fixed-length filename.
    private string PathFor(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_diskDir!, Convert.ToHexString(hash) + ".json");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort */ }
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _entries)
            if (kv.Value.Expires <= now)
                _entries.TryRemove(kv.Key, out _);

        // Still full (all entries live) → drop one arbitrary entry to bound memory.
        if (_entries.Count >= MaxEntries)
            foreach (var key in _entries.Keys)
            {
                _entries.TryRemove(key, out _);
                break;
            }
    }

    private readonly record struct Entry(DateTime Expires, IReadOnlyList<TavilyResult> Items);

    private sealed record DiskEntry(DateTime Expires, IReadOnlyList<TavilyResult> Items);
}
