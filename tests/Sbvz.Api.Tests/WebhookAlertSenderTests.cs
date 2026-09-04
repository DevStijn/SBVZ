using System.Net;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class WebhookAlertSenderTests
{
    [Fact]
    public async Task SendsOnlyTextAsJsonWithoutFollowingRedirects()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var sender = new WebhookAlertSender(
            client,
            Options.Create(
                new AlertWebhookOptions
                {
                    WebhookUrl = "https://alerts.example/secret-path"
                }));

        var result = await sender.SendAsync(
            new AlertNotification("fictional-alert", "Fictional alert text"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("{\"text\":\"Fictional alert text\"}", handler.Body);
    }

    [Fact]
    public async Task DoesNotRetryPermanentClientError()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.BadRequest);
        using var client = new HttpClient(handler);
        var sender = new WebhookAlertSender(
            client,
            Options.Create(
                new AlertWebhookOptions
                {
                    WebhookUrl = "https://alerts.example/secret-path"
                }));

        var result = await sender.SendAsync(
            new AlertNotification("fictional-alert", "Fictional alert text"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("HTTP 400", result.Failure);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class RecordingHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode);
        }
    }
}
