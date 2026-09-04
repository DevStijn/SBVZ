using Microsoft.Extensions.Options;
using Sbvz.Api.Safety;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Alerting;

internal sealed partial class SecurityAlertService(
    IAlertQueue queue,
    IOptions<AlertWebhookOptions> webhookOptions,
    IOptions<SbvzOptions> sbvzOptions,
    TimeProvider timeProvider,
    ILogger<SecurityAlertService> logger) : ISecurityAlertService
{
    private const int AuthenticationFailureThreshold = 5;
    private static readonly TimeSpan AuthenticationFailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AuthenticationAlertCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OperationalAlertCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CertificateAlertCooldown = TimeSpan.FromHours(23);

    private readonly Lock _gate = new();
    private readonly Dictionary<AuthenticationSurface, Queue<DateTimeOffset>> _authenticationFailures = [];
    private readonly Dictionary<string, DateTimeOffset> _lastAlerts = new(StringComparer.Ordinal);

    public void AuthenticationFailed(AuthenticationSurface surface)
    {
        if (!webhookOptions.Value.Enabled)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_authenticationFailures.TryGetValue(surface, out var failures))
            {
                failures = new Queue<DateTimeOffset>();
                _authenticationFailures.Add(surface, failures);
            }

            while (failures.TryPeek(out var occurredAt)
                && now - occurredAt > AuthenticationFailureWindow)
            {
                failures.Dequeue();
            }

            failures.Enqueue(now);

            if (failures.Count < AuthenticationFailureThreshold)
            {
                return;
            }

            failures.Clear();
            TryPublishLocked(
                $"authentication-failures-{surface}",
                "WARNING",
                $"{AuthenticationFailureThreshold} failed authentication attempts for {SurfaceLabel(surface)} within 10 minutes.",
                AuthenticationAlertCooldown,
                now);
        }
    }

    public void AuthenticationSucceeded(AuthenticationSurface surface)
    {
        Publish(
            $"authentication-succeeded-{surface}",
            "INFO",
            $"Authentication succeeded for {SurfaceLabel(surface)}.",
            TimeSpan.Zero);
    }

    public void RateLimitExceeded(AuthenticationSurface surface)
    {
        Publish(
            $"rate-limit-{surface}",
            "WARNING",
            $"Authentication rate limit reached for {SurfaceLabel(surface)}.",
            OperationalAlertCooldown);
    }

    public void AuditStorageUnavailable(AuditStorageOperation operation, Guid operationId)
    {
        Publish(
            "audit-storage-unavailable",
            "CRITICAL",
            $"Audit storage {operation.ToString().ToLowerInvariant()} failed. "
                + $"Operation {operationId:D} was stopped.",
            OperationalAlertCooldown);
    }

    public void SbvzRequestFailed(SbvzTechnicalFailure failure, Guid operationId)
    {
        var description = failure switch
        {
            SbvzTechnicalFailure.Timeout => "SBV-Z request timed out.",
            SbvzTechnicalFailure.TransportOrProtocol => "SBV-Z transport or protocol request failed.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };

        Publish(
            $"sbvz-request-{failure}",
            "WARNING",
            $"{description} Operation: {operationId:D}.",
            OperationalAlertCooldown);
    }

    public void ClientCertificateAlert(
        CertificateAlertLevel level,
        DateTimeOffset? expiresAtUtc = null)
    {
        var (severity, description) = level switch
        {
            CertificateAlertLevel.ExpiringWithinThirtyDays => (
                "WARNING",
                $"Client certificate expires within 30 days at {FormatTimestamp(expiresAtUtc)}."),
            CertificateAlertLevel.ExpiringWithinSevenDays => (
                "CRITICAL",
                $"Client certificate expires within 7 days at {FormatTimestamp(expiresAtUtc)}."),
            CertificateAlertLevel.Expired => (
                "CRITICAL",
                $"Client certificate expired at {FormatTimestamp(expiresAtUtc)}."),
            CertificateAlertLevel.Unavailable => (
                "CRITICAL",
                "Client certificate could not be loaded."),
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };

        Publish(
            $"client-certificate-{level}",
            severity,
            description,
            CertificateAlertCooldown);
    }

    public void EmergencyStopChanged(EmergencyStopStatus status)
    {
        var (severity, description) = status switch
        {
            EmergencyStopStatus.Active => (
                "CRITICAL",
                "Emergency stop is active; BSN operations are blocked."),
            EmergencyStopStatus.Inactive => (
                "INFO",
                "Emergency stop was cleared; BSN operations are available."),
            EmergencyStopStatus.Unavailable => (
                "CRITICAL",
                "Emergency stop status is unavailable; BSN operations are blocked."),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

        Publish(
            $"emergency-stop-{status}",
            severity,
            description,
            OperationalAlertCooldown);
    }

    public void EmergencyAccessUsed(string operation, Guid operationId)
    {
        Publish(
            $"emergency-access-{operation}-{operationId:D}",
            "CRITICAL",
            $"Emergency access was used for {operation}. Operation: {operationId:D}.",
            TimeSpan.Zero);
    }

    private void Publish(string key, string severity, string description, TimeSpan cooldown)
    {
        if (!webhookOptions.Value.Enabled)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        lock (_gate)
        {
            TryPublishLocked(key, severity, description, cooldown, now);
        }
    }

    private void TryPublishLocked(
        string key,
        string severity,
        string description,
        TimeSpan cooldown,
        DateTimeOffset now)
    {
        if (_lastAlerts.TryGetValue(key, out var lastAlert)
            && now - lastAlert < cooldown)
        {
            return;
        }

        var notification = new AlertNotification(
            key,
            $"SBV-Z [{sbvzOptions.Value.Mode}] {severity}: {description} Time: {now:O}");

        if (!queue.TryEnqueue(notification))
        {
            LogQueueFull(logger, key);
            return;
        }

        _lastAlerts[key] = now;
    }

    private static string SurfaceLabel(AuthenticationSurface surface)
    {
        return surface switch
        {
            AuthenticationSurface.InternalApi => "the internal API",
            AuthenticationSurface.AuditPortal => "the audit portal",
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };
    }

    private static string FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToUniversalTime().ToString("O") ?? "an unknown time";
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Security alert queue is full; alert {AlertKey} was dropped.")]
    private static partial void LogQueueFull(ILogger logger, string alertKey);
}
