using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gateway;

/// <summary>
/// Validates the HMAC-signed tokens minted by auth-service. The secret,
/// issuer and "plan" claim name are the shared contract between the two.
/// </summary>
public sealed class JwtValidator
{
    public const string PlanClaim = "plan";

    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;

    public JwtValidator(IConfiguration config)
    {
        var secret = config["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is required.");

        _parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = config["JWT_ISSUER"] ?? "urlshortener-auth",
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public async Task<UserIdentity?> ValidateAsync(string token)
    {
        var result = await _handler.ValidateTokenAsync(token, _parameters);
        if (!result.IsValid)
            return null;

        var sub = result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var s) ? s?.ToString() : null;
        if (sub is null)
            return null;

        var plan = result.Claims.TryGetValue(PlanClaim, out var p) ? p?.ToString() : null;
        return new UserIdentity(sub, plan ?? "free");
    }
}

public sealed record UserIdentity(string UserId, string Plan);
