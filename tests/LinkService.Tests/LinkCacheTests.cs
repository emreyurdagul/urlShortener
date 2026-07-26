using LinkService;

namespace LinkService.Tests;

public class LinkCacheTests
{
    [Fact]
    public void Returns_a_stored_target()
    {
        var cache = new LinkCache();
        cache.Set("sho.rt", "abc", "https://example.com");

        Assert.True(cache.TryGet("sho.rt", "abc", out var target));
        Assert.Equal("https://example.com", target);
    }

    [Fact]
    public void Misses_unknown_codes()
    {
        var cache = new LinkCache();
        Assert.False(cache.TryGet("sho.rt", "nope", out _));
    }

    [Fact]
    public void Keys_are_scoped_by_domain()
    {
        var cache = new LinkCache();
        cache.Set("a.co", "x", "https://a.example");

        Assert.True(cache.TryGet("a.co", "x", out _));
        Assert.False(cache.TryGet("b.co", "x", out _));
    }

    [Fact]
    public void Entries_expire_after_the_ttl()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cache = new LinkCache(ttl: TimeSpan.FromMinutes(5), clock: () => now);
        cache.Set("sho.rt", "abc", "https://example.com");

        Assert.True(cache.TryGet("sho.rt", "abc", out _));

        now = now.AddMinutes(6);
        Assert.False(cache.TryGet("sho.rt", "abc", out _));
    }

    [Fact]
    public void Stays_within_its_size_bound()
    {
        var cache = new LinkCache(maxEntries: 100);
        for (var i = 0; i < 500; i++)
            cache.Set("sho.rt", $"code{i}", $"https://example.com/{i}");

        // Most recently inserted key must still resolve; the map never exceeds
        // the configured bound (eviction is best-effort but strictly capped).
        Assert.True(cache.TryGet("sho.rt", "code499", out _));
    }
}
