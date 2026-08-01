namespace Monolith;

/// <summary>
/// Turns a bearer token into an in-process identity stored on HttpContext.Items.
///
/// Contrast with the gateway's AuthMiddleware: there the identity had to be handed
/// to a SEPARATE process, so it was serialized into X-User-* headers and any
/// client-supplied copy was stripped first to keep them trustworthy. Here the
/// identity never leaves the process — downstream layers read a typed object
/// directly, so there is no header to forge and nothing to strip.
///
/// Kept behavior: a token that is present but invalid/expired is rejected with
/// 401 rather than silently continuing as anonymous.
/// </summary>
public sealed class AuthMiddleware(RequestDelegate next, JwtService jwt, ILogger<AuthMiddleware> logger)
{
    public const string ItemKey = "user";

    public async Task InvokeAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            UserIdentity? identity = null;
            try
            {
                identity = await jwt.ValidateAsync(token);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Token validation threw: {Message}", ex.Message);
            }

            if (identity is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Items[ItemKey] = identity;
        }

        await next(context);
    }
}
