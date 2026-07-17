namespace Gateway;

public sealed class Backend(string url)
{
    // Starts healthy so traffic flows before the first probe completes.
    private int _healthy = 1;

    public string Url { get; } = url;

    public bool IsHealthy => Volatile.Read(ref _healthy) == 1;

    /// <summary>Returns true only when this call actually changed the state.</summary>
    public bool SetHealthy(bool healthy)
    {
        var value = healthy ? 1 : 0;
        return Interlocked.Exchange(ref _healthy, value) != value;
    }
}
