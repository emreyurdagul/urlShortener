using System.Collections.Concurrent;
using System.Diagnostics;

namespace Monolith;

/// <summary>
/// A from-scratch token bucket keyed per client. Each key refills at a steady
/// rate up to a burst capacity; a request costs one token. Time is read from a
/// monotonic clock so it is immune to wall-clock jumps. (Unchanged from the
/// gateway build — rate limiting is just as valid a concern in a monolith.)
/// </summary>
public sealed class TokenBucketRateLimiter(int capacity, double refillPerSecond)
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    public bool TryAcquire(string key) =>
        _buckets.GetOrAdd(key, _ => new Bucket(capacity))
            .TryTake(capacity, refillPerSecond, Stopwatch.GetTimestamp());

    public int TrackedKeys => _buckets.Count;

    private sealed class Bucket(double initialTokens)
    {
        private readonly Lock _gate = new();
        private double _tokens = initialTokens;
        private long _lastStamp = Stopwatch.GetTimestamp();

        public bool TryTake(int capacity, double refillPerSecond, long nowStamp)
        {
            lock (_gate)
            {
                var elapsedSeconds = Math.Max(0, (nowStamp - _lastStamp) / (double)Stopwatch.Frequency);
                _tokens = Math.Min(capacity, _tokens + elapsedSeconds * refillPerSecond);
                _lastStamp = nowStamp;

                if (_tokens < 1)
                    return false;

                _tokens -= 1;
                return true;
            }
        }
    }
}
