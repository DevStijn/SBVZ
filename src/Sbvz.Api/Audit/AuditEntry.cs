namespace Sbvz.Api.Audit;

public sealed record AuditEntry(
    int SchemaVersion,
    Guid EventId,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset OperationStartedAtUtc,
    string OperationId,
    string? TraceId,
    bool Invalidated,
    string SubscriberNumber,
    string? PatientReference,
    string? RecordId,
    string? ApiClientId,
    AuditActor Actor,
    AuditAccess Access,
    AuditOperation Operation,
    AuditExchange Exchange)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record AuditActor(
    string Id,
    string Role);

public sealed record AuditAccess(
    bool Authorized,
    bool? TreatmentRelationship,
    bool? Consent,
    bool EmergencyAccess);

public sealed record AuditOperation(
    string Name,
    string Purpose,
    AuditActionType ActionType,
    AuditDataCategory DataCategory,
    AuditOutcome Outcome);

public sealed record AuditExchange(
    string? ResponseCode,
    int? DurationMilliseconds);

public enum AuditOutcome
{
    Attempted,
    Succeeded,
    Failed,
    Cancelled
}

public enum AuditActionType
{
    Read,
    Query,
    Security
}

public enum AuditDataCategory
{
    PatientIdentification,
    AuditLog,
    Service
}
