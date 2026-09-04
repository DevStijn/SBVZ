namespace Sbvz.Api.Audit;

public interface IAuditReader
{
    Task<AuditPage> ReadPageAsync(
        DateOnly auditDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public sealed record AuditPage(
    IReadOnlyList<AuditOperationRecord> Entries,
    int Page,
    int PageSize,
    int TotalPages,
    int TotalCount);

public sealed record AuditOperationRecord(
    int SchemaVersion,
    Guid AttemptEventId,
    Guid? CompletionEventId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string OperationId,
    string? TraceId,
    bool Invalidated,
    string SubscriberNumber,
    string? PatientReference,
    string? RecordId,
    string ActorId,
    string ActorRole,
    bool Authorized,
    bool? TreatmentRelationship,
    bool? Consent,
    bool EmergencyAccess,
    string OperationName,
    string Purpose,
    AuditActionType ActionType,
    AuditDataCategory DataCategory,
    AuditOutcome Outcome,
    string? ResponseCode,
    int? DurationMilliseconds);
