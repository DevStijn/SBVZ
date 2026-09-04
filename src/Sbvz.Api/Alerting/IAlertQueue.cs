namespace Sbvz.Api.Alerting;

internal interface IAlertQueue
{
    bool TryEnqueue(AlertNotification notification);

    IAsyncEnumerable<AlertNotification> ReadAllAsync(CancellationToken cancellationToken);
}
