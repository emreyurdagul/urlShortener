using Microsoft.Extensions.Primitives;

namespace Gateway;

/// <summary>
/// Hop-by-hop headers describe a single TCP connection and must not be
/// forwarded to the backend (RFC 9110 §7.6.1).
/// </summary>
public static class ProxyHeaders
{
    private static readonly HashSet<string> HopByHop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "Proxy-Connection", "TE", "Trailer", "Transfer-Encoding", "Upgrade",
    };

    public static bool IsHopByHop(string name) => HopByHop.Contains(name);

    /// <summary>
    /// The Connection header can name additional headers; those become
    /// hop-by-hop for this exchange as well.
    /// </summary>
    public static HashSet<string> ConnectionTokens(StringValues connectionHeader)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in connectionHeader)
        {
            if (string.IsNullOrEmpty(value))
                continue;

            foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                tokens.Add(token);
        }

        return tokens;
    }

    public static bool ShouldSkip(string name, IReadOnlySet<string> connectionTokens) =>
        IsHopByHop(name) || connectionTokens.Contains(name);
}
