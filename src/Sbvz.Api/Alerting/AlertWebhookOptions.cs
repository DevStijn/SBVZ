namespace Sbvz.Api.Alerting;

internal sealed class AlertWebhookOptions
{
    public const string WebhookUrlVariable = "SBVZ_ALERT_WEBHOOK_URL";
    public const string WebhookUrlFileVariable = "SBVZ_ALERT_WEBHOOK_URL_FILE";

    public string WebhookUrl { get; set; } = string.Empty;

    public bool Enabled => !string.IsNullOrWhiteSpace(WebhookUrl);
}
