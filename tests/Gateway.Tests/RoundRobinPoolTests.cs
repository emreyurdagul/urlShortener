using System.Collections.Concurrent;

namespace Gateway.Tests;

public class RoundRobinPoolTests
{
    [Fact]
    public void Next_cycles_through_backends_in_order()
    {
        var pool = new RoundRobinPool("test", ["a", "b", "c"]);
        var results = Enumerable.Range(0, 6).Select(_ => pool.Next().Url).ToArray();
        Assert.Equal(new[] { "a", "b", "c", "a", "b", "c" }, results);
    }

    [Fact]
    public async Task Next_distributes_evenly_under_concurrency()
    {
        var backends = new[] { "a", "b", "c", "d" };
        var pool = new RoundRobinPool("test", backends);
        var counts = new ConcurrentDictionary<string, int>();

        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                counts.AddOrUpdate(pool.Next().Url, 1, (_, c) => c + 1);
        }));
        await Task.WhenAll(tasks);

        // Interlocked ticketing guarantees an exact split: 8000 calls over 4 backends.
        Assert.All(backends, b => Assert.Equal(2000, counts[b]));
    }

    [Fact]
    public void Unhealthy_backends_are_skipped()
    {
        var pool = new RoundRobinPool("test", ["a", "b", "c"]);
        pool.Backends.Single(b => b.Url == "b").SetHealthy(false);

        var results = Enumerable.Range(0, 6).Select(_ => pool.Next().Url).ToArray();

        Assert.DoesNotContain("b", results);
        Assert.Contains("a", results);
        Assert.Contains("c", results);
    }

    [Fact]
    public void Recovered_backend_rejoins_rotation()
    {
        var pool = new RoundRobinPool("test", ["a", "b"]);
        var b = pool.Backends.Single(x => x.Url == "b");

        b.SetHealthy(false);
        _ = pool.Next();
        b.SetHealthy(true);

        var results = Enumerable.Range(0, 4).Select(_ => pool.Next().Url).ToArray();
        Assert.Contains("b", results);
    }

    [Fact]
    public void Fails_open_when_every_backend_is_down()
    {
        var pool = new RoundRobinPool("test", ["a", "b"]);
        foreach (var backend in pool.Backends)
            backend.SetHealthy(false);

        // A broken health checker must not blackhole the pool on its own.
        Assert.NotNull(pool.Next());
    }

    [Fact]
    public void Empty_pool_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new RoundRobinPool("test", []));
    }
}
