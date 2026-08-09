using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Both System.IdentityModel.Tokens.Jwt and Microsoft.IdentityModel.JsonWebTokens
// define JwtRegisteredClaimNames (same string values); merging the mint + validate
// halves pulls in both, so pin one to disambiguate.
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace Monolith;

/// <summary>
/// Issues AND validates the HMAC-signed JWTs.
///
/// In the microservice build this was SPLIT across two services: auth-service's
/// TokenService minted tokens, the gateway's JwtValidator validated them, and the
/// shared secret + claim names formed a cross-service contract. In one process
/// both halves live in a single class — the "contract" is just this type.
/// </summary>
public sealed class JwtService
{
    public const string PlanClaim = "plan";

    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _validation;
    private readonly string _issuer;
    private readonly TimeSpan _lifetime;
    private readonly JwtSecurityTokenHandler _writer = new();
    private readonly JsonWebTokenHandler _reader = new();

    public JwtService(IConfiguration config)
    {
        var secret = config["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is required.");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("JWT_SECRET must be at least 32 bytes.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _issuer = config["JWT_ISSUER"] ?? "urlshortener-mono";
        // Short-lived access token; refresh tokens keep the session alive.
        _lifetime = TimeSpan.FromMinutes(double.TryParse(config["JWT_LIFETIME_MINUTES"], out var m) ? m : 15);

        _validation = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    public (string Token, DateTime ExpiresAt) Issue(long userId, string email, string plan, DateTime nowUtc)
    {
        var expires = nowUtc.Add(_lifetime);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(PlanClaim, plan),
            ],
            notBefore: nowUtc,
            expires: expires,
            signingCredentials: _credentials);

        return (_writer.WriteToken(token), expires);
    }

    public async Task<UserIdentity?> ValidateAsync(string token)
    {
        var result = await _reader.ValidateTokenAsync(token, _validation);
        if (!result.IsValid)
            return null;

        if (!result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var sub) ||
            !long.TryParse(sub?.ToString(), out var userId))
            return null;

        var plan = result.Claims.TryGetValue(PlanClaim, out var p) ? p?.ToString() : null;
        return new UserIdentity(userId, plan ?? Plans.Free);
    }
}
