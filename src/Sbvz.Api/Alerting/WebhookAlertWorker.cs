namespace Sbvz.Api.Alerting;

internal sealed partial class WebhookAlertWorker(
    IAlertQueue queue,
    WebhookAlertSender sender,
    ILogger<WebhookAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in queue.ReadAllAsync(stoppingToken))
        {
            var result = await sender.SendAsync(notification, stoppingToken);

            if (!result.Success)
            {
                LogDeliveryFailed(logger, notification.Key, result.Failure ?? "unknown error");
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Security alert {AlertKey} could not be delivered ({Failure}).")]
    private static partial void LogDeliveryFailed(
        ILogger logger,
        string alertKey,
        string failure);
}
