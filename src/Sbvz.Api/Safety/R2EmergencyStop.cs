using System.Diagnostics;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Audit;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Safety;

internal sealed partial class R2EmergencyStop(
    IEmergencyStopObjectStore objectStore,
    IOptions<EmergencyStopOptions> emergencyStopOptions,
    IOptions<SbvzOptions> sbvzOptions,
    IAuditWriter auditWriter,
    ISecurityAlertService alerts,
    TimeProvider timeProvider,
    ILogger<R2EmergencyStop> logger) : IEmergencyStop, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    private static readonly AuditActor SystemActor = new("system", "service");
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private EmergencyStopStatus? _status;
    private DateTimeOffset _checkedAtUtc;

    public async Task<EmergencyStopStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        if (_status is not null && now - _checkedAtUtc < CacheDuration)
        {
            return _status.Value;
        }

        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            now = timeProvider.GetUtcNow();

            if (_status is not null && now - _checkedAtUtc < CacheDuration)
            {
                return _status.Value;
            }

            var previous = _status;
            var current = await ReadStatusAsync(cancellationToken);
            _status = current;
            _checkedAtUtc = now;

            if (previous != current)
            {
                await ReportStatusAsync(
                    SystemActor,
                    current,
                    notify: previous is not null || current is not EmergencyStopStatus.Inactive);
            }

            return current;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task ActivateAsync(
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(actor.Id) || string.IsNullOrWhiteSpace(actor.Role))
        {
            throw new ArgumentException("The emergency-stop actor must have an ID and role.", nameof(actor));
        }

        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            try
            {
                await objectStore.CreateIfMissingAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is AmazonS3Exception
                or HttpRequestException
                or OperationCanceledException)
            {
                _status = EmergencyStopStatus.Unavailable;
                _checkedAtUtc = timeProvider.GetUtcNow();
                LogActivationFailed(logger, exception);
                await ReportStatusAsync(
                    SystemActor,
                    EmergencyStopStatus.Unavailable,
                    notify: true);
                throw new EmergencyStopActivationException(
                    "The emergency stop could not be activated.",
                    exception);
            }

            _status = EmergencyStopStatus.Active;
            _checkedAtUtc = timeProvider.GetUtcNow();
            await ReportStatusAsync(actor, EmergencyStopStatus.Active, notify: true);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
    }

    private async Task<EmergencyStopStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await objectStore.ExistsAsync(cancellationToken)
                ? EmergencyStopStatus.Active
                : EmergencyStopStatus.Inactive;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is AmazonS3Exception
            or HttpRequestException
            or OperationCanceledException)
        {
            LogCheckFailed(logger, exception);

            return EmergencyStopStatus.Unavailable;
        }
    }

    private async Task ReportStatusAsync(
        AuditActor actor,
        EmergencyStopStatus current,
        bool notify)
    {
        if (notify)
        {
            alerts.EmergencyStopChanged(current);
        }

        var operationId = Guid.CreateVersion7();
        var operationStartedAtUtc = timeProvider.GetUtcNow();
        var operationReference = operationId.ToString("D");

        try
        {
            await auditWriter.WriteAsync(
                CreateAuditEntry(
                    operationReference,
                    operationStartedAtUtc,
                    actor,
                    AuditOutcome.Attempted,
                    responseCode: null),
                CancellationToken.None);
            await auditWriter.WriteAsync(
                CreateAuditEntry(
                    operationReference,
                    operationStartedAtUtc,
                    actor,
                    current is EmergencyStopStatus.Unavailable
                        ? AuditOutcome.Failed
                        : AuditOutcome.Succeeded,
                    current.ToString().ToLowerInvariant()),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogAuditFailed(logger, exception, operationId);
            alerts.AuditStorageUnavailable(AuditStorageOperation.Write, operationId);
        }
    }

    private AuditEntry CreateAuditEntry(
        string operationId,
        DateTimeOffset operationStartedAtUtc,
        AuditActor actor,
        AuditOutcome outcome,
        string? responseCode)
    {
        return new AuditEntry(
            AuditEntry.CurrentSchemaVersion,
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            operationStartedAtUtc,
            operationId,
            Activity.Current?.Id,
            Invalidated: false,
            sbvzOptions.Value.SubscriberNumber,
            PatientReference: null,
            RecordId: emergencyStopOptions.Value.ObjectKey,
            ApiClientId: null,
            actor,
            new AuditAccess(
                Authorized: true,
                TreatmentRelationship: null,
                Consent: null,
                EmergencyAccess: false),
            new AuditOperation(
                "emergency-stop",
                "service-protection",
                AuditActionType.Security,
                AuditDataCategory.Service,
                outcome),
            new AuditExchange(responseCode, DurationMilliseconds: null));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Emergency stop status check failed.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Emergency stop activation failed.")]
    private static partial void LogActivationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Emergency stop transition audit failed for operation {OperationId}.")]
    private static partial void LogAuditFailed(
        ILogger logger,
        Exception exception,
        Guid operationId);
}
