using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Tests;

public class JwtValidatorTests
{
    private const string Secret = "this-is-a-test-secret-at-least-32-bytes!!";
    private const string Issuer = "urlshortener-auth";

    private static JwtValidator BuildValidator() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT_SECRET"] = Secret,
            ["JWT_ISSUER"] = Issuer,
        }).Build());

    private static string Sign(string secret, string issuer, DateTime expires, string plan = "premium")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var token = new JwtSecurityToken(
            issuer: issuer,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "42"),
                new Claim("plan", plan),
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task Accepts_a_valid_token_and_extracts_identity()
    {
        var token = Sign(Secret, Issuer, DateTime.UtcNow.AddHours(1), plan: "premium");

        var identity = await BuildValidator().ValidateAsync(token);

        Assert.NotNull(identity);
        Assert.Equal("42", identity.UserId);
        Assert.Equal("premium", identity.Plan);
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_the_wrong_secret()
    {
        var token = Sign("a-completely-different-secret-32-bytes-x", Issuer, DateTime.UtcNow.AddHours(1));

        Assert.Null(await BuildValidator().ValidateAsync(token));
    }

    [Fact]
    public async Task Rejects_an_expired_token()
    {
        var token = Sign(Secret, Issuer, DateTime.UtcNow.AddHours(-1));

        Assert.Null(await BuildValidator().ValidateAsync(token));
    }

    [Fact]
    public async Task Rejects_a_token_from_the_wrong_issuer()
    {
        var token = Sign(Secret, "someone-else", DateTime.UtcNow.AddHours(1));

        Assert.Null(await BuildValidator().ValidateAsync(token));
    }
}
