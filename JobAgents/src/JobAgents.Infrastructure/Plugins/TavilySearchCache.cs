using System.Collections.Concurrent;
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
/// Process-wide cache of Tavily responses plus a per-run whole-web fallback budget, to cut the number
/// of real HTTP requests the agents send. Responses are keyed by the full request signature and expire
/// after a short TTL, so identical queries within a run — and across "Search harder" re-runs — reuse
/// one response instead of re-querying Tavily. Registered as a singleton.
/// </summary>
public sealed class TavilySearchCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private const int MaxEntries = 512;
    // Generous per-run guard: the whole-web fallback is how VN sites get found, so we only cap runaway
    // doubling rather than starve recall. Distinct repeats are already absorbed by the response cache.
    private const int MaxFallbacksPerRun = 4;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<RunId, int> _fallbacks = new();

    public bool TryGet(string key, out IReadOnlyList<TavilyResult> items)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > DateTime.UtcNow)
        {
            items = entry.Items;
            return true;
        }

        items = Array.Empty<TavilyResult>();
        return false;
    }

    public void Set(string key, IReadOnlyList<TavilyResult> items)
    {
        if (_entries.Count >= MaxEntries)
            Prune();

        _entries[key] = new Entry(DateTime.UtcNow.Add(Ttl), items);
    }

    /// <summary>True while this run is still within its whole-web fallback budget.</summary>
    public bool TryUseFallback(RunId runId) =>
        _fallbacks.AddOrUpdate(runId, 1, static (_, n) => n + 1) <= MaxFallbacksPerRun;

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
}
