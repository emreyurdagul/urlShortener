namespace Monolith;

/// <summary>
/// Plan tiers and the quota each unlocks. In the microservice build this logic
/// was DUPLICATED across two services (auth-service owned the tiers, link-service
/// re-declared the quota) precisely because they were independently deployable.
/// In one process there is a single source of truth.
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
