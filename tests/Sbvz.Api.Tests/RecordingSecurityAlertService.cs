using Sbvz.Api.Alerting;
using Sbvz.Api.Safety;

namespace Sbvz.Api.Tests;

internal sealed class RecordingSecurityAlertService : ISecurityAlertService
{
    public List<AuthenticationSurface> AuthenticationFailures { get; } = [];
    public List<AuthenticationSurface> AuthenticationSuccesses { get; } = [];
    public List<AuthenticationSurface> RateLimits { get; } = [];
    public List<(AuditStorageOperation Operation, Guid OperationId)> AuditStorageFailures { get; } = [];
    public List<(SbvzTechnicalFailure Failure, Guid OperationId)> SbvzFailures { get; } = [];
    public List<(CertificateAlertLevel Level, DateTimeOffset? ExpiresAtUtc)> CertificateAlerts { get; } = [];
    public List<EmergencyStopStatus> EmergencyStopChanges { get; } = [];
    public List<(string Operation, Guid OperationId)> EmergencyAccessUses { get; } = [];

    public void AuthenticationFailed(AuthenticationSurface surface)
    {
        AuthenticationFailures.Add(surface);
    }

    public void AuthenticationSucceeded(AuthenticationSurface surface)
    {
        AuthenticationSuccesses.Add(surface);
    }

    public void RateLimitExceeded(AuthenticationSurface surface)
    {
        RateLimits.Add(surface);
    }

    public void AuditStorageUnavailable(AuditStorageOperation operation, Guid operationId)
    {
        AuditStorageFailures.Add((operation, operationId));
    }

    public void SbvzRequestFailed(SbvzTechnicalFailure failure, Guid operationId)
    {
        SbvzFailures.Add((failure, operationId));
    }

    public void ClientCertificateAlert(
        CertificateAlertLevel level,
        DateTimeOffset? expiresAtUtc = null)
    {
        CertificateAlerts.Add((level, expiresAtUtc));
    }

    public void EmergencyStopChanged(EmergencyStopStatus status)
    {
        EmergencyStopChanges.Add(status);
    }

    public void EmergencyAccessUsed(string operation, Guid operationId)
    {
        EmergencyAccessUses.Add((operation, operationId));
    }
}
