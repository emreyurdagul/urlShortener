using Microsoft.Extensions.Primitives;

namespace Gateway.Tests;

public class ProxyHeadersTests
{
    private static readonly HashSet<string> NoConnectionTokens = new(StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Connection")]
    [InlineData("keep-alive")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Upgrade")]
    [InlineData("TE")]
    [InlineData("Proxy-Authorization")]
    public void Standard_hop_by_hop_headers_are_skipped(string name)
    {
        Assert.True(ProxyHeaders.ShouldSkip(name, NoConnectionTokens));
    }

    [Theory]
    [InlineData("Accept")]
    [InlineData("Authorization")]
    [InlineData("Content-Type")]
    [InlineData("X-Forwarded-For")]
    public void End_to_end_headers_are_forwarded(string name)
    {
        Assert.False(ProxyHeaders.ShouldSkip(name, NoConnectionTokens));
    }

    [Fact]
    public void Headers_named_in_connection_header_become_hop_by_hop()
    {
        var tokens = ProxyHeaders.ConnectionTokens(new StringValues("close, X-Trace-Id"));

        Assert.True(ProxyHeaders.ShouldSkip("x-trace-id", tokens));
        Assert.False(ProxyHeaders.ShouldSkip("Accept", tokens));
    }
}
