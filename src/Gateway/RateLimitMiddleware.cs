using Prometheus;

namespace Gateway;

/// <summary>
/// Per-client rate limiting. Authenticated callers are keyed by user id;
/// anonymous callers fall back to their remote IP. Over-limit requests get a
/// 429 with Retry-After instead of reaching a backend.
/// </summary>
public sealed class RateLimitMiddleware(RequestDelegate next, TokenBucketRateLimiter limiter)
{
    private static readonly Counter Rejected = Metrics.CreateCounter(
        "gateway_rate_limited_total", "Requests rejected by the rate limiter.");

    public async Task InvokeAsync(HttpContext context)
    {
        var key = context.Items["user"] is UserIdentity user
            ? $"user:{user.UserId}"
            : $"ip:{context.Connection.RemoteIpAddress}";

        if (!limiter.TryAcquire(key))
        {
            Rejected.Inc();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            return;
        }

        await next(context);
    }
}
