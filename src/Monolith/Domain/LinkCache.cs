using System.Collections.Concurrent;
using Prometheus;

namespace Monolith;

/// <summary>
/// In-memory cache for the redirect hot path. A short code's target URL is
/// immutable once created, so a resolved lookup can skip Postgres entirely on
/// every subsequent hit. Bounded by entry count and TTL so memory stays flat
/// under churn; only positive lookups are cached (a miss may be a code that is
/// about to be created).
///
/// Unchanged from the microservice build — the redirect hot path benefits from
/// this cache regardless of architecture.
/// </summary>
public sealed class LinkCache
{
    private readonly record struct Entry(string Target, long ExpiresTicks);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly int _maxEntries;
    private readonly long _ttlTicks;
    private readonly Func<long> _nowTicks;

    private static readonly Counter Hits = Metrics.CreateCounter(
        "link_cache_hits_total", "Redirect lookups served from the in-memory cache.");
    private static readonly Counter Misses = Metrics.CreateCounter(
        "link_cache_misses_total", "Redirect lookups that fell through to Postgres.");

    public LinkCache(int maxEntries = 50_000, TimeSpan? ttl = null, Func<DateTime>? clock = null)
    {
        _maxEntries = maxEntries;
        _ttlTicks = (ttl ?? TimeSpan.FromMinutes(10)).Ticks;
        var now = clock ?? (() => DateTime.UtcNow);
        _nowTicks = () => now().Ticks;
    }

    private static string Key(string domain, string code) => $"{domain}\n{code}";

    public bool TryGet(string domain, string code, out string target)
    {
        if (_entries.TryGetValue(Key(domain, code), out var entry) && entry.ExpiresTicks > _nowTicks())
        {
            target = entry.Target;
            Hits.Inc();
            return true;
        }

        target = "";
        Misses.Inc();
        return false;
    }

    public void Set(string domain, string code, string target)
    {
        if (_entries.Count >= _maxEntries)
            Evict();
        _entries[Key(domain, code)] = new Entry(target, _nowTicks() + _ttlTicks);
    }

    /// <summary>Drops an entry after an edit or delete.</summary>
    public void Remove(string domain, string code) => _entries.TryRemove(Key(domain, code), out _);

    /// <summary>
    /// Best-effort bound: drop expired entries first, then a slice of arbitrary
    /// ones if still at capacity. Not strict LRU, but keeps memory flat without
    /// locking the whole map.
    /// </summary>
    private void Evict()
    {
        var now = _nowTicks();
        foreach (var (key, entry) in _entries)
            if (entry.ExpiresTicks <= now)
                _entries.TryRemove(key, out _);

        if (_entries.Count < _maxEntries)
            return;

        var toDrop = Math.Max(1, _maxEntries / 10);
        foreach (var key in _entries.Keys)
        {
            if (toDrop-- <= 0)
                break;
            _entries.TryRemove(key, out _);
        }
    }
}
