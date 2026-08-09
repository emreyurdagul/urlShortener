using System.Security.Cryptography;
using System.Text;

namespace Monolith;

/// <summary>
/// Opaque, rotating, revocable refresh tokens. Only the SHA-256 hash is stored,
/// so a DB leak can't be replayed. See AuthAppService for issue/rotate/revoke.
/// </summary>
public static class RefreshTokens
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromDays(30);

    public static (string Raw, string Hash) New()
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
