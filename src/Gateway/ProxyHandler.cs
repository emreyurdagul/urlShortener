using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Primitives;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Gateway;

public sealed class ProxyHandler(RouteTable routes, ILogger<ProxyHandler> logger)
{
    private static readonly TimeSpan BackendTimeout = TimeSpan.FromSeconds(10);

    // Client span per backend hop; the name is registered via AddSource("Gateway.Proxy").
    private static readonly ActivitySource Trace = new("Gateway.Proxy");

    // Explicit W3C propagator — the global Propagators.DefaultTextMapPropagator can
    // be a no-op depending on init order, so inject with a concrete one.
    private static readonly TraceContextPropagator Propagator = new();

    private static readonly HttpMessageInvoker Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    });

    public async Task HandleAsync(HttpContext context)
    {
        var pool = routes.Match(context.Request.Path);
        if (pool is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var hasBody = context.Request.ContentLength is > 0 ||
                      context.Request.Headers.ContainsKey("Transfer-Encoding");
        // A consumed request body cannot be replayed, so requests with a body
        // get a single attempt; bodyless requests fail over across the pool.
        var maxAttempts = hasBody ? 1 : pool.Backends.Count;

        for (var attempt = 1; ; attempt++)
        {
            var backend = pool.Next();
            var stopwatch = Stopwatch.StartNew();
            using var proxyActivity = Trace.StartActivity("proxy backend", ActivityKind.Client);
            proxyActivity?.SetTag("gateway.pool", pool.Name);
            proxyActivity?.SetTag("gateway.backend", backend.Url);
            proxyActivity?.SetTag("http.request.method", context.Request.Method);
            using var request = BuildRequest(context, backend.Url, hasBody);

            // The timeout covers connect + backend processing up to response
            // headers; streaming a large body afterwards is only bounded by
            // the client.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeoutCts.CancelAfter(BackendTimeout);

            HttpResponseMessage response;
            try
            {
                response = await Client.SendAsync(request, timeoutCts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                if (context.RequestAborted.IsCancellationRequested)
                    return;

                // Connection-level failures mean the request never reached the
                // backend: safe to eject the backend and retry elsewhere.
                var neverReachedBackend = ex is HttpRequestException
                {
                    HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError,
                };

                if (neverReachedBackend && backend.SetHealthy(false))
                {
                    GatewayMetrics.BackendHealthy.WithLabels(pool.Name, backend.Url).Set(0);
                    logger.LogWarning("Backend {Backend} ejected after a connection failure", backend.Url);
                }

                if (neverReachedBackend && attempt < maxAttempts)
                {
                    GatewayMetrics.Failovers.WithLabels(pool.Name, backend.Url).Inc();
                    logger.LogWarning("{Method} {Path} -> {Backend} unreachable, failing over (attempt {Attempt}/{Max})",
                        context.Request.Method, context.Request.Path, backend.Url, attempt, maxAttempts);
                    continue;
                }

                context.Response.StatusCode = ex is OperationCanceledException
                    ? StatusCodes.Status504GatewayTimeout
                    : StatusCodes.Status502BadGateway;
                RecordRequest(pool, backend, context, context.Response.StatusCode, stopwatch);
                logger.LogWarning("{Method} {Path} -> {Backend} failed after {Elapsed}ms: {Reason}",
                    context.Request.Method, context.Request.Path, backend.Url,
                    stopwatch.ElapsedMilliseconds, ex.Message);
                return;
            }

            using (response)
            {
                context.Response.StatusCode = (int)response.StatusCode;
                CopyResponseHeaders(response, context.Response);

                try
                {
                    await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return; // client went away mid-body
                }

                RecordRequest(pool, backend, context, (int)response.StatusCode, stopwatch);
                logger.LogInformation("{Method} {Path}{Query} -> {Backend} {Status} in {Elapsed}ms",
                    context.Request.Method, context.Request.Path, context.Request.QueryString,
                    backend.Url, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
            }

            return;
        }
    }

    private static void RecordRequest(IBackendPool pool, Backend backend, HttpContext context, int statusCode, Stopwatch stopwatch)
    {
        GatewayMetrics.Requests
            .WithLabels(pool.Name, backend.Url, context.Request.Method, statusCode.ToString())
            .Inc();
        GatewayMetrics.RequestDuration
            .WithLabels(pool.Name, backend.Url)
            .Observe(stopwatch.Elapsed.TotalSeconds);
    }

    private static HttpRequestMessage BuildRequest(HttpContext context, string backendUrl, bool hasBody)
    {
        var incoming = context.Request;
        var targetUri = new Uri(new Uri(backendUrl), incoming.Path + incoming.QueryString);
        var request = new HttpRequestMessage(new HttpMethod(incoming.Method), targetUri);

        if (hasBody)
            request.Content = new StreamContent(incoming.Body);

        var connectionTokens = ProxyHeaders.ConnectionTokens(incoming.Headers.Connection);
        foreach (var (name, values) in incoming.Headers)
        {
            if (ProxyHeaders.ShouldSkip(name, connectionTokens) || IsProxyOwnedHeader(name))
                continue;

            if (!request.Headers.TryAddWithoutValidation(name, values.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(name, values.ToArray());
        }

        request.Headers.Host = targetUri.Authority;

        var forwardedFor = incoming.Headers["X-Forwarded-For"].ToList();
        if (context.Connection.RemoteIpAddress is { } remoteIp)
            forwardedFor.Add(remoteIp.ToString());
        if (forwardedFor.Count > 0)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", string.Join(", ", forwardedFor));

        // Honor an upstream proxy's scheme (e.g. Traefik terminating TLS in
        // front of us) rather than our own http hop; only synthesize it when no
        // trusted proxy set it.
        var forwardedProto = incoming.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? incoming.Scheme;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", forwardedProto);
        if (incoming.Host.HasValue)
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", incoming.Host.Value);

        // Propagate the current trace context to the backend. The raw
        // HttpMessageInvoker doesn't auto-inject W3C headers the way HttpClient
        // does, so the gateway does it explicitly — this is what stitches the
        // backend's spans into the same trace as the gateway's.
        request.Headers.Remove("traceparent");
        request.Headers.Remove("tracestate");
        if (Activity.Current is { } current)
            Propagator.Inject(
                new PropagationContext(current.Context, Baggage.Current),
                request.Headers,
                static (headers, key, value) => headers.TryAddWithoutValidation(key, value));

        return request;
    }

    /// <summary>Headers the gateway rewrites itself instead of copying through.</summary>
    private static bool IsProxyOwnedHeader(string name) =>
        name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Forwarded-For", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("X-Forwarded-Host", StringComparison.OrdinalIgnoreCase);

    private static void CopyResponseHeaders(HttpResponseMessage response, HttpResponse output)
    {
        var connectionTokens = response.Headers.TryGetValues("Connection", out var connection)
            ? ProxyHeaders.ConnectionTokens(new StringValues(connection.ToArray()))
            : [];

        foreach (var (name, values) in response.Headers)
        {
            if (!ProxyHeaders.ShouldSkip(name, connectionTokens))
                output.Headers[name] = values.ToArray();
        }

        foreach (var (name, values) in response.Content.Headers)
        {
            if (!ProxyHeaders.ShouldSkip(name, connectionTokens))
                output.Headers[name] = values.ToArray();
        }
    }
}
