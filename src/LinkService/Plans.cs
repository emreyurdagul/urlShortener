namespace LinkService;

/// <summary>
/// Plan → capabilities, enforced by link-service from the trusted X-User-Plan
/// header the gateway injects. Deliberately duplicated (not shared with
/// auth-service) so the two services stay independently deployable.
///
///   tier   shortest code   links        vanity
///   free   5               50           no
///   plus   3               unlimited    no
///   pro    1               unlimited    yes
///
/// Higher tiers also allow everything a lower tier can (the min only drops).
/// </summary>
public static class Plans
{
    public const string Free = "free";
    public const string Plus = "plus";
    public const string Pro = "pro";

    /// <summary>Maps legacy/unknown values onto a current tier (old "premium" == pro).</summary>
    public static string Normalize(string? plan) => plan switch
    {
        Free or Plus or Pro => plan!,
        "premium" => Pro,
        _ => Free,
    };

    /// <summary>Max links a user may own. null = unlimited.</summary>
    public static int? LinkQuota(string plan) => Normalize(plan) == Free ? 50 : null;

    /// <summary>Shortest random code length this tier may request.</summary>
    public static int MinCodeLength(string plan) => Normalize(plan) switch
    {
        Pro => 1,
        Plus => 3,
        _ => 5,
    };

    /// <summary>Whether this tier may choose its own (vanity) code.</summary>
    public static bool AllowsVanity(string plan) => Normalize(plan) == Pro;
}
