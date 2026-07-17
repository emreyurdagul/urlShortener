namespace Gateway;

/// <summary>
/// Turns a bearer token into trusted identity headers for the backends.
/// Any client-supplied X-User-* header is stripped first, so backends can
/// trust these headers precisely because only the gateway sets them.
/// </summary>
public sealed class AuthMiddleware(RequestDelegate next, JwtValidator validator, ILogger<AuthMiddleware> logger)
{
    public const string UserIdHeader = "X-User-Id";
    public const string UserPlanHeader = "X-User-Plan";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.Headers.Remove(UserIdHeader);
        context.Request.Headers.Remove(UserPlanHeader);

        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            UserIdentity? identity = null;
            try
            {
                identity = await validator.ValidateAsync(token);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Token validation threw: {Message}", ex.Message);
            }

            if (identity is not null)
            {
                context.Request.Headers[UserIdHeader] = identity.UserId;
                context.Request.Headers[UserPlanHeader] = identity.Plan;
                context.Items["user"] = identity;
            }
            else
            {
                // A token was presented but is invalid/expired — reject rather
                // than silently forwarding as anonymous.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }
}
