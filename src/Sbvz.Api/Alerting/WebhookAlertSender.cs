using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Alerting;

internal sealed class WebhookAlertSender(
    HttpClient httpClient,
    IOptions<AlertWebhookOptions> options)
{
    private const int MaximumAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public async Task<WebhookDeliveryResult> SendAsync(
        AlertNotification notification,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return WebhookDeliveryResult.Successful;
        }

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    options.Value.WebhookUrl)
                {
                    Content = JsonContent.Create(new WebhookPayload(notification.Text))
                };
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return WebhookDeliveryResult.Successful;
                }

                if (attempt < MaximumAttempts && IsTransient(response.StatusCode))
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                    continue;
                }

                return new WebhookDeliveryResult(
                    Success: false,
                    Failure: $"HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == MaximumAttempts)
                {
                    return new WebhookDeliveryResult(Success: false, Failure: "timeout");
                }
            }
            catch (HttpRequestException)
            {
                if (attempt == MaximumAttempts)
                {
                    return new WebhookDeliveryResult(Success: false, Failure: "transport error");
                }
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        return new WebhookDeliveryResult(Success: false, Failure: "delivery failed");
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private sealed record WebhookPayload(
        [property: JsonPropertyName("text")] string Text);
}

internal sealed record WebhookDeliveryResult(bool Success, string? Failure)
{
    public static WebhookDeliveryResult Successful { get; } = new(Success: true, Failure: null);
}
