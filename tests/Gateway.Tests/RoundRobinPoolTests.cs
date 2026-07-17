using System.Collections.Concurrent;

namespace Gateway.Tests;

public class RoundRobinPoolTests
{
    [Fact]
    public void Next_cycles_through_backends_in_order()
    {
        var pool = new RoundRobinPool("test", ["a", "b", "c"]);
        var results = Enumerable.Range(0, 6).Select(_ => pool.Next()).ToArray();
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
                counts.AddOrUpdate(pool.Next(), 1, (_, c) => c + 1);
        }));
        await Task.WhenAll(tasks);

        // Interlocked ticketing guarantees an exact split: 8000 calls over 4 backends.
        Assert.All(backends, b => Assert.Equal(2000, counts[b]));
    }

    [Fact]
    public void Empty_pool_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new RoundRobinPool("test", []));
    }
}
