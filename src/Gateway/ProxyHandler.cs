using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Primitives;

namespace Gateway;

public sealed class ProxyHandler(RouteTable routes, ILogger<ProxyHandler> logger)
{
    private static readonly TimeSpan BackendTimeout = TimeSpan.FromSeconds(10);

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

        var backend = pool.Next();
        using var request = BuildRequest(context, backend);
        var stopwatch = Stopwatch.StartNew();

        // The timeout covers connect + backend processing up to response headers;
        // streaming a large body afterwards is only bounded by the client.
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

            context.Response.StatusCode = ex is OperationCanceledException
                ? StatusCodes.Status504GatewayTimeout
                : StatusCodes.Status502BadGateway;
            logger.LogWarning("{Method} {Path} -> {Backend} failed after {Elapsed}ms: {Reason}",
                context.Request.Method, context.Request.Path, backend,
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

            logger.LogInformation("{Method} {Path}{Query} -> {Backend} {Status} in {Elapsed}ms",
                context.Request.Method, context.Request.Path, context.Request.QueryString,
                backend, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
        }
    }

    private static HttpRequestMessage BuildRequest(HttpContext context, string backend)
    {
        var incoming = context.Request;
        var targetUri = new Uri(new Uri(backend), incoming.Path + incoming.QueryString);
        var request = new HttpRequestMessage(new HttpMethod(incoming.Method), targetUri);

        if (incoming.ContentLength is > 0 || incoming.Headers.ContainsKey("Transfer-Encoding"))
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

        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", incoming.Scheme);
        if (incoming.Host.HasValue)
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", incoming.Host.Value);

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
