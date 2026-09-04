using System.Threading.Channels;

namespace Sbvz.Api.Alerting;

internal sealed class AlertQueue : IAlertQueue
{
    private const int Capacity = 100;
    private readonly Channel<AlertNotification> _channel = Channel.CreateBounded<AlertNotification>(
        new BoundedChannelOptions(Capacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(AlertNotification notification)
    {
        return _channel.Writer.TryWrite(notification);
    }

    public IAsyncEnumerable<AlertNotification> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
