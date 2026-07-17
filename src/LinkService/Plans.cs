namespace LinkService;

/// <summary>
/// Plan → link quota. Duplicated (deliberately small) rather than shared with
/// auth-service so the two services stay independently deployable; the plan
/// itself arrives as a trusted header from the gateway.
/// </summary>
public static class Plans
{
    /// <summary>Max links a user may own. null means unlimited.</summary>
    public static int? LinkQuota(string plan) => plan switch
    {
        "premium" => null,
        _ => 50,
    };
}
