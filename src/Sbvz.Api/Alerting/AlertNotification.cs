namespace Sbvz.Api.Alerting;

internal sealed record AlertNotification(string Key, string Text);

internal enum AuthenticationSurface
{
    InternalApi,
    AuditPortal
}

internal enum AuditStorageOperation
{
    Read,
    Write
}

internal enum SbvzTechnicalFailure
{
    Timeout,
    TransportOrProtocol
}

internal enum CertificateAlertLevel
{
    ExpiringWithinThirtyDays,
    ExpiringWithinSevenDays,
    Expired,
    Unavailable
}
