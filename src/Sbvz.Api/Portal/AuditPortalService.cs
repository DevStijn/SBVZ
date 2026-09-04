using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Audit;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Portal;

public sealed partial class AuditPortalService
{
    private readonly IAuditReader _auditReader;
    private readonly IAuditWriter _auditWriter;
    private readonly IAuditPortalCredentialValidator _credentialValidator;
    private readonly IOptions<SbvzOptions> _sbvzOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ISecurityAlertService _alerts;
    private readonly ILogger<AuditPortalService> _logger;

    internal AuditPortalService(
        IAuditReader auditReader,
        IAuditWriter auditWriter,
        IAuditPortalCredentialValidator credentialValidator,
        IOptions<SbvzOptions> sbvzOptions,
        TimeProvider timeProvider,
        ISecurityAlertService alerts,
        ILogger<AuditPortalService> logger)
    {
        _auditReader = auditReader;
        _auditWriter = auditWriter;
        _credentialValidator = credentialValidator;
        _sbvzOptions = sbvzOptions;
        _timeProvider = timeProvider;
        _alerts = alerts;
        _logger = logger;
    }

    public async Task<bool> AuthenticateAsync(
        string username,
        string password,
        string totpCode)
    {
        var operationId = Guid.CreateVersion7();
        var operationReference = operationId.ToString("D");
        var operationStartedAtUtc = _timeProvider.GetUtcNow();
        var traceId = Activity.Current?.Id;

        await WriteAuditAsync(
            CreateAuditEntry(
                operationReference,
                "anonymous",
                recordId: null,
                "portal-login",
                "portal-authentication",
                AuditActionType.Security,
                AuditDataCategory.Service,
                operationStartedAtUtc,
                traceId,
                AuditOutcome.Attempted,
                responseCode: null,
                durationMilliseconds: null),
            operationId);

        var stopwatch = Stopwatch.StartNew();
        var isValid = _credentialValidator.Validate(username, password, totpCode);
        stopwatch.Stop();

        await WriteAuditAsync(
            CreateAuditEntry(
                operationReference,
                isValid ? username : "anonymous",
                recordId: null,
                "portal-login",
                "portal-authentication",
                AuditActionType.Security,
                AuditDataCategory.Service,
                operationStartedAtUtc,
                traceId,
                isValid ? AuditOutcome.Succeeded : AuditOutcome.Failed,
                isValid ? "success" : "invalid-credentials",
                stopwatch.ElapsedMilliseconds),
            operationId);

        if (isValid)
        {
            _alerts.AuthenticationSucceeded(AuthenticationSurface.AuditPortal);
        }
        else
        {
            _alerts.AuthenticationFailed(AuthenticationSurface.AuditPortal);
        }

        return isValid;
    }

    public async Task<AuditPage> ReadAsync(
        string username,
        DateOnly date,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.CreateVersion7();
        var operationReference = operationId.ToString("D");
        var operationStartedAtUtc = _timeProvider.GetUtcNow();
        var traceId = Activity.Current?.Id;
        var recordId = $"audit-date:{date:yyyy-MM-dd}";

        await WriteAuditAsync(
            CreateAuditEntry(
                operationReference,
                username,
                recordId,
                "view-audit",
                "audit-review",
                AuditActionType.Read,
                AuditDataCategory.AuditLog,
                operationStartedAtUtc,
                traceId,
                AuditOutcome.Attempted,
                responseCode: null,
                durationMilliseconds: null),
            operationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _auditReader.ReadPageAsync(
                date,
                page,
                pageSize,
                cancellationToken);
            stopwatch.Stop();

            await WriteAuditAsync(
                CreateAuditEntry(
                    operationReference,
                    username,
                    recordId,
                    "view-audit",
                    "audit-review",
                    AuditActionType.Read,
                    AuditDataCategory.AuditLog,
                    operationStartedAtUtc,
                    traceId,
                    AuditOutcome.Succeeded,
                    "success",
                    stopwatch.ElapsedMilliseconds),
                operationId);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            await WriteAuditAsync(
                CreateAuditEntry(
                    operationReference,
                    username,
                    recordId,
                    "view-audit",
                    "audit-review",
                    AuditActionType.Read,
                    AuditDataCategory.AuditLog,
                    operationStartedAtUtc,
                    traceId,
                    AuditOutcome.Cancelled,
                    "cancelled",
                    stopwatch.ElapsedMilliseconds),
                operationId);
            throw;
        }
        catch (AuditPortalUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            LogAuditReadFailed(exception, operationId);
            _alerts.AuditStorageUnavailable(AuditStorageOperation.Read, operationId);
            await WriteAuditAsync(
                CreateAuditEntry(
                    operationReference,
                    username,
                    recordId,
                    "view-audit",
                    "audit-review",
                    AuditActionType.Read,
                    AuditDataCategory.AuditLog,
                    operationStartedAtUtc,
                    traceId,
                    AuditOutcome.Failed,
                    "audit-read-error",
                    stopwatch.ElapsedMilliseconds),
                operationId);
            throw new AuditPortalUnavailableException(
                operationId,
                "Audit storage is unavailable.",
                exception);
        }
    }

    private AuditEntry CreateAuditEntry(
        string operationId,
        string username,
        string? recordId,
        string operationName,
        string purpose,
        AuditActionType actionType,
        AuditDataCategory dataCategory,
        DateTimeOffset operationStartedAtUtc,
        string? traceId,
        AuditOutcome outcome,
        string? responseCode,
        long? durationMilliseconds)
    {
        return new AuditEntry(
            AuditEntry.CurrentSchemaVersion,
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            operationStartedAtUtc,
            operationId,
            traceId,
            Invalidated: false,
            _sbvzOptions.Value.SubscriberNumber,
            PatientReference: null,
            recordId,
            new AuditActor(username, AuditPortalConstants.AdministratorRole),
            new AuditAccess(
                Authorized: true,
                TreatmentRelationship: null,
                Consent: null,
                EmergencyAccess: false),
            new AuditOperation(
                operationName,
                purpose,
                actionType,
                dataCategory,
                outcome),
            new AuditExchange(
                responseCode,
                durationMilliseconds is null
                    ? null
                    : checked((int)Math.Min(durationMilliseconds.Value, int.MaxValue))));
    }

    private async Task WriteAuditAsync(AuditEntry entry, Guid operationId)
    {
        try
        {
            await _auditWriter.WriteAsync(entry, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogAuditWriteFailed(exception, operationId);
            _alerts.AuditStorageUnavailable(AuditStorageOperation.Write, operationId);
            throw new AuditPortalUnavailableException(
                operationId,
                "Audit storage is unavailable.",
                exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Audit write failed for operation {OperationId}.")]
    private partial void LogAuditWriteFailed(Exception exception, Guid operationId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Audit read failed for operation {OperationId}.")]
    private partial void LogAuditReadFailed(Exception exception, Guid operationId);
}

internal sealed class AuditPortalUnavailableException(
    Guid operationId,
    string message,
    Exception innerException) : Exception(message, innerException)
{
    public Guid OperationId { get; } = operationId;
}
