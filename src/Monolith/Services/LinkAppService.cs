using Npgsql;
using Prometheus;

namespace Monolith;

/// <summary>
/// Application/business layer for links: creation (with quota + code-collision
/// retry), listing, QR lookup, and redirect resolution (cache-first). This merges
/// what the old link-service did, minus anything that only existed to talk to
/// other services over the network.
/// </summary>
public sealed class LinkAppService(
    LinkRepository links,
    LinkCache cache,
    ClickRecorder clicks,
    DomainConfig domains)
{
    private static readonly Counter Created = Metrics.CreateCounter(
        "links_created_total", "Short links created.",
        new CounterConfiguration { LabelNames = ["domain"] });
    private static readonly Counter QuotaRejected = Metrics.CreateCounter(
        "links_quota_rejected_total", "Link creations rejected because the owner hit their plan quota.");

    public DomainConfig Domains => domains;

    public sealed record CreateResult(bool Ok, string? Error, int StatusCode, string? Code, string? Domain);

    public async Task<CreateResult> CreateAsync(CreateLinkRequest req, UserIdentity? owner)
    {
        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var target) ||
            target.Scheme is not ("http" or "https"))
            return new CreateResult(false, "url must be an absolute http(s) URL.", 400, null, null);

        var domain = (req.Domain ?? domains.Default).ToLowerInvariant();
        if (!domains.Contains(domain))
            return new CreateResult(false, $"domain '{domain}' is not served here.", 400, null, null);

        var plan = Plans.Normalize(owner?.Plan);

        // Plan quota applies to owned links (random or vanity alike). An anonymous
        // request (no owner) skips it and stores a null owner_id.
        if (owner is { } o)
        {
            var quota = Plans.LinkQuota(plan);
            if (quota is { } limit)
            {
                var owned = await links.CountByOwnerAsync(o.UserId);
                if (owned >= limit)
                {
                    QuotaRejected.Inc();
                    return new CreateResult(false,
                        $"Plan '{plan}' allows {limit} links; upgrade for more.", 403, null, null);
                }
            }
        }

        // Vanity code (Pro): caller-chosen, so a collision is a hard 409 rather
        // than a retry.
        if (!string.IsNullOrWhiteSpace(req.Code))
        {
            if (!Plans.AllowsVanity(plan))
                return new CreateResult(false, "Custom codes are a Pro feature.", 403, null, null);
            if (!CodeGenerator.IsValidVanity(req.Code))
                return new CreateResult(false, "Custom code must be 1-32 chars of letters, digits, '-' or '_'.", 400, null, null);

            try
            {
                await links.InsertAsync(domain, req.Code, target.AbsoluteUri, owner?.UserId);
                Created.WithLabels(domain).Inc();
                return new CreateResult(true, null, 201, req.Code, domain);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return new CreateResult(false, $"'{req.Code}' is already taken.", 409, null, null);
            }
        }

        // Random code: the requested length must be within the caller's tier.
        var codeLength = req.CodeLength ?? 7;
        if (codeLength is < CodeGenerator.MinLength or > CodeGenerator.MaxLength)
            return new CreateResult(false,
                $"codeLength must be between {CodeGenerator.MinLength} and {CodeGenerator.MaxLength}.", 400, null, null);
        var minLength = Plans.MinCodeLength(plan);
        if (codeLength < minLength)
            return new CreateResult(false,
                $"{codeLength}-char codes need a higher plan; '{plan}' starts at {minLength}.", 403, null, null);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = CodeGenerator.Generate(codeLength);
            try
            {
                await links.InsertAsync(domain, code, target.AbsoluteUri, owner?.UserId);
                Created.WithLabels(domain).Inc();
                return new CreateResult(true, null, 201, code, domain);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Random code collided with an existing one; retry with a fresh code.
            }
        }

        return new CreateResult(false, "Could not allocate a unique code, please try again.", 500, null, null);
    }

    public Task<IReadOnlyList<LinkRow>> ListAsync(long ownerId) => links.ListByOwnerAsync(ownerId);

    public Task<(string Domain, string Code)> FindForQrAsync(string code) => links.FindForQrAsync(code);

    /// <summary>
    /// Resolves a short code to its target, cache-first, and records the click off
    /// the hot path. Returns null when the code does not exist.
    /// </summary>
    public async Task<string?> ResolveAsync(string domain, string code, ClickEvent click)
    {
        if (!cache.TryGet(domain, code, out var target))
        {
            var found = await links.FindTargetAsync(domain, code);
            if (found is null)
                return null;
            target = found;
            cache.Set(domain, code, target);
        }

        clicks.TryRecord(click); // fire-and-forget; never blocks the redirect
        return target;
    }
}
