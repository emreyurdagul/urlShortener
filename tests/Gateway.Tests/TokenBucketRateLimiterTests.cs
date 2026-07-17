namespace Gateway.Tests;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void Allows_up_to_burst_capacity_then_rejects()
    {
        // No refill: exactly `capacity` requests should pass.
        var limiter = new TokenBucketRateLimiter(capacity: 5, refillPerSecond: 0);

        var allowed = Enumerable.Range(0, 8).Count(_ => limiter.TryAcquire("k"));

        Assert.Equal(5, allowed);
    }

    [Fact]
    public void Keys_are_isolated()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 1, refillPerSecond: 0);

        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("b"));   // different key, own bucket
        Assert.False(limiter.TryAcquire("a"));  // "a" is now empty
    }

    [Fact]
    public async Task Refills_over_time()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 1, refillPerSecond: 50);

        Assert.True(limiter.TryAcquire("k"));
        Assert.False(limiter.TryAcquire("k"));

        await Task.Delay(100); // ~5 tokens worth of refill, capped at 1
        Assert.True(limiter.TryAcquire("k"));
    }

    [Fact]
    public async Task Concurrent_callers_never_exceed_capacity()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 100, refillPerSecond: 0);
        var allowed = 0;

        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
                if (limiter.TryAcquire("shared"))
                    Interlocked.Increment(ref allowed);
        }));
        await Task.WhenAll(tasks);

        Assert.Equal(100, allowed);
    }
}
