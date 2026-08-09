using System.Net;
using System.Net.Sockets;

namespace LinkService;

/// <summary>
/// Basic anti-SSRF / anti-abuse guard: rejects targets that resolve to private,
/// loopback, or link-local addresses so short links can't point at internal
/// infrastructure. Hostnames are resolved and every resolved address is checked.
/// </summary>
public static class LinkSafety
{
    public static async Task<bool> IsPublicAsync(Uri uri, CancellationToken ct = default)
    {
        if (IPAddress.TryParse(uri.Host, out var literal))
            return IsPublic(literal);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, timeout.Token);
            return addresses.Length > 0 && addresses.All(IsPublic);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return false; // unresolvable or timed out → treat as unsafe
        }
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return false;

        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] switch
            {
                0 or 10 => false,                       // this-network, 10.0.0.0/8
                127 => false,                           // loopback (also caught above)
                169 when b[1] == 254 => false,          // 169.254.0.0/16 link-local
                172 when b[1] is >= 16 and <= 31 => false, // 172.16.0.0/12
                192 when b[1] == 168 => false,          // 192.168.0.0/16
                100 when b[1] is >= 64 and <= 127 => false, // 100.64.0.0/10 CGNAT
                _ => true,
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return false;
            if ((b[0] & 0xFE) == 0xFC)                  // fc00::/7 unique-local
                return false;
            return true;
        }

        return false;
    }
}
