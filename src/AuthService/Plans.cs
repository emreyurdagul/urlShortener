namespace AuthService;

/// <summary>
/// Plan tiers: a free tier plus two paid tiers (Plus, Pro). The tier decides the
/// link quota, the shortest code length unlocked, and whether vanity codes are
/// allowed — all of which link-service enforces from the trusted plan header.
/// Here auth-service only needs to validate and normalize tier names.
///
/// Payment for the paid tiers is simulated (see /api/auth/upgrade) — the upgrade
/// flow is real (DB + fresh token) but no money changes hands.
/// </summary>
public static class Plans
{
    public const string Free = "free";
    public const string Plus = "plus";
    public const string Pro = "pro";

    public static bool IsValid(string plan) => plan is Free or Plus or Pro;

    /// <summary>Maps legacy/unknown values onto a current tier (old "premium" == pro).</summary>
    public static string Normalize(string? plan) => plan switch
    {
        Free or Plus or Pro => plan!,
        "premium" => Pro,
        _ => Free,
    };
}
