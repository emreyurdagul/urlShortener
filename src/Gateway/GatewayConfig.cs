using System.Text.Json;

namespace Gateway;

public sealed record GatewayConfig
{
    public required List<RouteConfig> Routes { get; init; }
    public required Dictionary<string, List<string>> Pools { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static GatewayConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        var config = JsonSerializer.Deserialize<GatewayConfig>(stream, JsonOptions)
                     ?? throw new InvalidOperationException($"Gateway config could not be parsed: {path}");

        if (config.Routes.Count == 0)
            throw new InvalidOperationException("Gateway config has no routes.");

        foreach (var route in config.Routes)
        {
            if (!config.Pools.TryGetValue(route.Pool, out var backends) || backends.Count == 0)
                throw new InvalidOperationException(
                    $"Route '{route.PathPrefix}' references pool '{route.Pool}' which is missing or empty.");
        }

        return config;
    }
}

public sealed record RouteConfig
{
    public required string PathPrefix { get; init; }
    public required string Pool { get; init; }
}
