using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class SecurityAlertServiceTests
{
    [Fact]
    public void QueuesAlertOnlyAfterFiveAuthenticationFailures()
    {
        var queue = new RecordingAlertQueue();
        var service = CreateService(queue);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            service.AuthenticationFailed(AuthenticationSurface.AuditPortal);
        }

        Assert.Empty(queue.Notifications);

        service.AuthenticationFailed(AuthenticationSurface.AuditPortal);

        var notification = Assert.Single(queue.Notifications);
        Assert.Contains("5 failed authentication attempts", notification.Text, StringComparison.Ordinal);
        Assert.Contains("audit portal", notification.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("username", notification.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuppressesRepeatedOperationalAlertDuringCooldown()
    {
        var queue = new RecordingAlertQueue();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(queue, timeProvider);
        var operationId = Guid.CreateVersion7();

        service.AuditStorageUnavailable(AuditStorageOperation.Read, operationId);
        service.AuditStorageUnavailable(AuditStorageOperation.Write, operationId);
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        service.AuditStorageUnavailable(AuditStorageOperation.Write, operationId);

        Assert.Equal(2, queue.Notifications.Count);
        Assert.All(
            queue.Notifications,
            notification => Assert.DoesNotContain("https://", notification.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void QueuesEverySuccessfulPortalAuthentication()
    {
        var queue = new RecordingAlertQueue();
        var service = CreateService(queue);

        service.AuthenticationSucceeded(AuthenticationSurface.AuditPortal);
        service.AuthenticationSucceeded(AuthenticationSurface.AuditPortal);

        Assert.Equal(2, queue.Notifications.Count);
        Assert.All(
            queue.Notifications,
            notification => Assert.Contains(
                "Authentication succeeded for the audit portal",
                notification.Text,
                StringComparison.Ordinal));
    }

    [Fact]
    public void QueuesEveryEmergencyAccessUseWithOperationReference()
    {
        var queue = new RecordingAlertQueue();
        var service = CreateService(queue);
        var firstOperationId = Guid.CreateVersion7();
        var secondOperationId = Guid.CreateVersion7();

        service.EmergencyAccessUsed("lookup-bsn", firstOperationId);
        service.EmergencyAccessUsed("lookup-bsn", secondOperationId);

        Assert.Equal(2, queue.Notifications.Count);
        Assert.Contains(firstOperationId.ToString("D"), queue.Notifications[0].Text, StringComparison.Ordinal);
        Assert.Contains(secondOperationId.ToString("D"), queue.Notifications[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotTrackOrQueueAlertsWithoutConfiguredWebhook()
    {
        var queue = new RecordingAlertQueue();
        var service = CreateService(queue, webhookUrl: string.Empty);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            service.AuthenticationFailed(AuthenticationSurface.InternalApi);
        }

        service.AuditStorageUnavailable(AuditStorageOperation.Write, Guid.CreateVersion7());

        Assert.Empty(queue.Notifications);
    }

    [Theory]
    [InlineData(31, null)]
    [InlineData(30, "ExpiringWithinThirtyDays")]
    [InlineData(7, "ExpiringWithinSevenDays")]
    [InlineData(0, "Expired")]
    public void ClassifiesCertificateExpiry(int daysRemaining, string? expected)
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        var result = ClientCertificateMonitor.DetermineAlertLevel(
            now,
            now.AddDays(daysRemaining));

        Assert.Equal(expected, result?.ToString());
    }

    private static SecurityAlertService CreateService(
        RecordingAlertQueue queue,
        MutableTimeProvider? timeProvider = null,
        string webhookUrl = "https://alerts.example/webhook")
    {
        return new SecurityAlertService(
            queue,
            Options.Create(new AlertWebhookOptions { WebhookUrl = webhookUrl }),
            Options.Create(
                new SbvzOptions
                {
                    Mode = nameof(SbvzMode.Production),
                    SubscriberNumber = "12345678"
                }),
            timeProvider ?? new MutableTimeProvider(
                new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<SecurityAlertService>.Instance);
    }

    private sealed class RecordingAlertQueue : IAlertQueue
    {
        public List<AlertNotification> Notifications { get; } = [];

        public bool TryEnqueue(AlertNotification notification)
        {
            Notifications.Add(notification);
            return true;
        }

        public IAsyncEnumerable<AlertNotification> ReadAllAsync(CancellationToken cancellationToken)
        {
            return Empty(cancellationToken);
        }

        private static async IAsyncEnumerable<AlertNotification> Empty(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
