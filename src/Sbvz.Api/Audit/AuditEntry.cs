namespace Sbvz.Api.Audit;

public sealed record AuditEntry(
    int SchemaVersion,
    Guid EventId,
    DateTimeOffset RegisteredAtUtc,
    string OperationId,
    string SubscriberNumber,
    string? PatientReference,
    string? RecordId,
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
    bool? TreatmentRelationship,
    bool? Consent,
    bool EmergencyAccess);

public sealed record AuditOperation(
    string Name,
    string Purpose,
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
