using Sbvz.Api.Safety;

namespace Sbvz.Api.Alerting;

internal interface ISecurityAlertService
{
    void AuthenticationFailed(AuthenticationSurface surface);

    void AuthenticationSucceeded(AuthenticationSurface surface);

    void RateLimitExceeded(AuthenticationSurface surface);

    void AuditStorageUnavailable(AuditStorageOperation operation, Guid operationId);

    void SbvzRequestFailed(SbvzTechnicalFailure failure, Guid operationId);

    void ClientCertificateAlert(CertificateAlertLevel level, DateTimeOffset? expiresAtUtc = null);

    void EmergencyStopChanged(EmergencyStopStatus status);

    void EmergencyAccessUsed(string operation, Guid operationId);
}
