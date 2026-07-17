namespace Gateway;

public interface IBackendPool
{
    string Name { get; }
    string Next();
}

public sealed class RoundRobinPool : IBackendPool
{
    private readonly IReadOnlyList<string> _backends;
    private int _counter = -1;

    public RoundRobinPool(string name, IReadOnlyList<string> backends)
    {
        if (backends.Count == 0)
            throw new ArgumentException("A pool needs at least one backend.", nameof(backends));

        Name = name;
        _backends = backends;
    }

    public string Name { get; }

    public string Next()
    {
        // uint cast keeps the index valid after the int counter overflows.
        var ticket = unchecked((uint)Interlocked.Increment(ref _counter));
        return _backends[(int)(ticket % _backends.Count)];
    }
}
