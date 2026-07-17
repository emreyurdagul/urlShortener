using System.Threading.Channels;

namespace LinkService;

public sealed record ClickEvent(string Domain, string Code, string? Referer, string? UserAgent, DateTime ClickedAt);

/// <summary>
/// Non-blocking sink for click events. The redirect hot path only enqueues;
/// a background service drains the channel and ships batches to
/// analytics-service. If the queue is full we drop rather than slow redirects
/// down — analytics is best-effort, redirects are not.
/// </summary>
public sealed class ClickRecorder
{
    private readonly Channel<ClickEvent> _channel;

    public ClickRecorder(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
    }

    public ChannelReader<ClickEvent> Reader => _channel.Reader;

    /// <summary>Enqueues without blocking; returns false if the buffer is full.</summary>
    public bool TryRecord(ClickEvent click) => _channel.Writer.TryWrite(click);
}
