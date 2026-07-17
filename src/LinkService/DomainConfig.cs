namespace LinkService;

public sealed class DomainConfig
{
    private readonly HashSet<string> _domains;

    public DomainConfig(IReadOnlyList<string> domains)
    {
        var normalized = domains
            .Select(d => d.Trim().ToLowerInvariant())
            .Where(d => d.Length > 0)
            .ToList();

        if (normalized.Count == 0)
            throw new ArgumentException("At least one domain must be configured.", nameof(domains));

        Default = normalized[0];
        _domains = [.. normalized];
    }

    public string Default { get; }

    public bool Contains(string domain) => _domains.Contains(domain.ToLowerInvariant());
}
