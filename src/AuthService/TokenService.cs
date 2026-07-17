using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AuthService;

/// <summary>
/// Issues HMAC-signed JWTs. The gateway holds the same secret and validates
/// them, so the signing algorithm and claim names are the contract between
/// the two services.
/// </summary>
public sealed class TokenService
{
    public const string PlanClaim = "plan";

    private readonly SigningCredentials _credentials;
    private readonly string _issuer;
    private readonly TimeSpan _lifetime;
    private readonly JwtSecurityTokenHandler _handler = new();

    public TokenService(IConfiguration config)
    {
        var secret = config["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is required.");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("JWT_SECRET must be at least 32 bytes.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _issuer = config["JWT_ISSUER"] ?? "urlshortener-auth";
        _lifetime = TimeSpan.FromHours(double.TryParse(config["JWT_LIFETIME_HOURS"], out var h) ? h : 24);
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

        return (_handler.WriteToken(token), expires);
    }
}
