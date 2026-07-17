namespace Gateway.Tests;

public class RouteTableTests
{
    private static RouteTable Build() => new(new GatewayConfig
    {
        Routes =
        [
            new RouteConfig { PathPrefix = "/", Pool = "links" },
            new RouteConfig { PathPrefix = "/api/analytics", Pool = "analytics" },
        ],
        Pools = new Dictionary<string, List<string>>
        {
            ["links"] = ["http://links:8080"],
            ["analytics"] = ["http://analytics:8080"],
        },
    });

    [Fact]
    public void Longest_prefix_wins()
    {
        Assert.Equal("analytics", Build().Match("/api/analytics/clicks")!.Name);
    }

    [Fact]
    public void Root_route_catches_everything_else()
    {
        Assert.Equal("links", Build().Match("/abc123")!.Name);
    }
}
