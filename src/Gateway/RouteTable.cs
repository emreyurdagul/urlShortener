namespace Gateway;

public sealed class RouteTable
{
    private readonly List<(PathString Prefix, IBackendPool Pool)> _routes;

    public RouteTable(GatewayConfig config)
    {
        var pools = config.Pools.ToDictionary(
            p => p.Key,
            p => (IBackendPool)new RoundRobinPool(p.Key, p.Value),
            StringComparer.OrdinalIgnoreCase);

        _routes = config.Routes
            .OrderByDescending(r => r.PathPrefix.Length)
            .Select(r => (new PathString(r.PathPrefix == "/" ? null : r.PathPrefix), pools[r.Pool]))
            .ToList();
    }

    public IBackendPool? Match(PathString path)
    {
        foreach (var (prefix, pool) in _routes)
        {
            if (!prefix.HasValue || path.StartsWithSegments(prefix))
                return pool;
        }

        return null;
    }
}
