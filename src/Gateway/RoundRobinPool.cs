namespace Gateway;

public interface IBackendPool
{
    string Name { get; }
    IReadOnlyList<Backend> Backends { get; }
    Backend Next();
}

public sealed class RoundRobinPool : IBackendPool
{
    private readonly Backend[] _backends;
    private int _counter = -1;

    public RoundRobinPool(string name, IReadOnlyList<string> backendUrls)
    {
        if (backendUrls.Count == 0)
            throw new ArgumentException("A pool needs at least one backend.", nameof(backendUrls));

        Name = name;
        _backends = backendUrls.Select(url => new Backend(url)).ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<Backend> Backends => _backends;

    /// <summary>
    /// Returns the next healthy backend in rotation. If every backend is
    /// marked down the pool fails open to plain rotation, so a broken health
    /// checker cannot take the whole pool offline by itself.
    /// </summary>
    public Backend Next()
    {
        for (var i = 0; i < _backends.Length; i++)
        {
            var candidate = NextByTicket();
            if (candidate.IsHealthy)
                return candidate;
        }

        return NextByTicket();
    }

    private Backend NextByTicket()
    {
        // uint cast keeps the index valid after the int counter overflows.
        var ticket = unchecked((uint)Interlocked.Increment(ref _counter));
        return _backends[(int)(ticket % _backends.Length)];
    }
}
