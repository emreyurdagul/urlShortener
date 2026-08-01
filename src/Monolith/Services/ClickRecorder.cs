using System.Threading.Channels;

namespace Monolith;

/// <summary>
/// Non-blocking sink for click events. The redirect hot path only enqueues; a
/// background writer drains the channel and persists batches. If the queue is
/// full we drop rather than slow redirects down — analytics is best-effort,
/// redirects are not.
///
/// Unchanged from the microservice build: async decoupling keeps the hot path
/// fast REGARDLESS of architecture. What changed is the drain target — a direct
/// DB write instead of an HTTP hop to a separate analytics service (see ClickWriter).
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
