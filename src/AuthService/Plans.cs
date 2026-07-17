namespace AuthService;

/// <summary>
/// The plan tiers. Kept deliberately tiny — payment and upgrades arrive in
/// build phase 6; here a plan only decides quota and which features unlock.
/// </summary>
public static class Plans
{
    public const string Free = "free";
    public const string Premium = "premium";

    public static bool IsValid(string plan) => plan is Free or Premium;

    /// <summary>Max links a user may own. null means unlimited.</summary>
    public static int? LinkQuota(string plan) => plan switch
    {
        Premium => null,
        _ => 50,
    };
}
