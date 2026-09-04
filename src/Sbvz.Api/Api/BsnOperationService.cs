using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Audit;
using Sbvz.Api.Safety;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Api;

internal sealed partial class BsnOperationService(
    ISbvzClient sbvzClient,
    IAuditWriter auditWriter,
    IPatientReferenceGenerator patientReferenceGenerator,
    IOptions<SbvzOptions> options,
    TimeProvider timeProvider,
    ISecurityAlertService alerts,
    IEmergencyStop emergencyStop,
    ILogger<BsnOperationService> logger)
{
    public Task<BsnOperationResponse> LookupAsync(
        BsnLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(request.Actor, request.Access, request.Purpose, request.RecordId);
        RequireObject(request.Person, "person");
        var query = CreateQuery(request.Person, request.Address, bsn: null);

        return ExecuteAsync(
            request.Actor,
            request.Access,
            request.RecordId,
            request.Purpose,
            "lookup-bsn",
            query,
            cancellationToken);
    }

    public Task<BsnOperationResponse> VerifyAsync(
        BsnVerifyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateContext(request.Actor, request.Access, request.Purpose, request.RecordId);
        RequireObject(request.Person, "person");
        RequireValue(request.Bsn, 9, "bsn");
        var query = CreateQuery(request.Person, request.Address, request.Bsn);

        return ExecuteAsync(
            request.Actor,
            request.Access,
            request.RecordId,
            request.Purpose,
            "verify-bsn",
            query,
            cancellationToken);
    }

    private async Task<BsnOperationResponse> ExecuteAsync(
        ApiActor actor,
        ApiAccessContext access,
        string? recordId,
        string purpose,
        string operation,
        SbvzPersonQuery query,
        CancellationToken cancellationToken)
    {
        var searchPath = SbvzQueryValidator.Validate(query);
        var operationId = Guid.CreateVersion7();
        var localReference = operationId.ToString("D");
        var operationStartedAtUtc = timeProvider.GetUtcNow();
        var traceId = Activity.Current?.Id;
        var patientReference = query.Bsn is null
            ? null
            : patientReferenceGenerator.CreateFromBsn(query.Bsn);

        await WriteAuditAsync(
            CreateAuditEntry(
                localReference,
                actor,
                access,
                recordId,
                purpose,
                operation,
                operationStartedAtUtc,
                traceId,
                patientReference,
                AuditOutcome.Attempted,
                responseCode: null,
                durationMilliseconds: null),
            operationId);

        if (!HasAccess(access))
        {
            await WriteAuditAsync(
                CreateAuditEntry(
                    localReference,
                    actor,
                    access,
                    recordId,
                    purpose,
                    operation,
                    operationStartedAtUtc,
                    traceId,
                    patientReference,
                    AuditOutcome.Failed,
                    responseCode: "access-refused",
                    durationMilliseconds: 0),
                operationId);
            throw new SbvzAccessDeniedException(operationId);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var emergencyStopStatus = await emergencyStop.GetStatusAsync(cancellationToken);

            if (emergencyStopStatus is not EmergencyStopStatus.Inactive)
            {
                stopwatch.Stop();
                await WriteAuditAsync(
                    CreateAuditEntry(
                        localReference,
                        actor,
                        access,
                        recordId,
                        purpose,
                        operation,
                        operationStartedAtUtc,
                        traceId,
                        patientReference,
                        AuditOutcome.Failed,
                        responseCode: emergencyStopStatus is EmergencyStopStatus.Active
                            ? "emergency-stop-active"
                            : "emergency-stop-unavailable",
                        durationMilliseconds: stopwatch.ElapsedMilliseconds),
                    operationId);
                throw new EmergencyStopException(operationId, emergencyStopStatus);
            }

            if (access.EmergencyAccess)
            {
                alerts.EmergencyAccessUsed(operation, operationId);
            }

            var response = await sbvzClient.QueryAsync(query, localReference, cancellationToken);
            stopwatch.Stop();
            var responseBsn = response.Answer?.Person?.Bsn;
            patientReference ??= responseBsn is null
                ? null
                : patientReferenceGenerator.CreateFromBsn(responseBsn);
            var outcome = response.Result is SbvzResult.Error
                ? AuditOutcome.Failed
                : AuditOutcome.Succeeded;

            await WriteAuditAsync(
                CreateAuditEntry(
                    localReference,
                    actor,
                    access,
                    recordId,
                    purpose,
                    operation,
                    operationStartedAtUtc,
                    traceId,
                    patientReference,
                    outcome,
                    responseCode: CreateResponseCode(response),
                    durationMilliseconds: stopwatch.ElapsedMilliseconds),
                operationId);

            return new BsnOperationResponse(
                operationId,
                ToApiSearchPath(searchPath),
                response.Result,
                response.Answer,
                response.Messages);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            await WriteAuditAsync(
                CreateAuditEntry(
                    localReference,
                    actor,
                    access,
                    recordId,
                    purpose,
                    operation,
                    operationStartedAtUtc,
                    traceId,
                    patientReference,
                    AuditOutcome.Cancelled,
                    responseCode: "cancelled",
                    durationMilliseconds: stopwatch.ElapsedMilliseconds),
                operationId);
            throw;
        }
        catch (TaskCanceledException exception)
        {
            stopwatch.Stop();
            await WriteAuditAsync(
                CreateAuditEntry(
                    localReference,
                    actor,
                    access,
                    recordId,
                    purpose,
                    operation,
                    operationStartedAtUtc,
                    traceId,
                    patientReference,
                    AuditOutcome.Failed,
                    responseCode: "timeout",
                    durationMilliseconds: stopwatch.ElapsedMilliseconds),
                operationId);
            LogSbvzTimeout(logger, exception, operationId);
            alerts.SbvzRequestFailed(SbvzTechnicalFailure.Timeout, operationId);
            throw new SbvzOperationException(
                operationId,
                SbvzOperationFailure.Timeout,
                "SBV-Z request timed out.",
                exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or SbvzProtocolException)
        {
            stopwatch.Stop();
            await WriteAuditAsync(
                CreateAuditEntry(
                    localReference,
                    actor,
                    access,
                    recordId,
                    purpose,
                    operation,
                    operationStartedAtUtc,
                    traceId,
                    patientReference,
                    AuditOutcome.Failed,
                    responseCode: "transport-or-protocol-error",
                    durationMilliseconds: stopwatch.ElapsedMilliseconds),
                operationId);
            LogSbvzFailure(logger, exception, operationId);
            alerts.SbvzRequestFailed(SbvzTechnicalFailure.TransportOrProtocol, operationId);
            throw new SbvzOperationException(
                operationId,
                SbvzOperationFailure.Upstream,
                "SBV-Z request failed.",
                exception);
        }
    }

    private AuditEntry CreateAuditEntry(
        string operationId,
        ApiActor actor,
        ApiAccessContext access,
        string? recordId,
        string purpose,
        string operation,
        DateTimeOffset operationStartedAtUtc,
        string? traceId,
        string? patientReference,
        AuditOutcome outcome,
        string? responseCode,
        long? durationMilliseconds)
    {
        return new AuditEntry(
            AuditEntry.CurrentSchemaVersion,
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            operationStartedAtUtc,
            operationId,
            traceId,
            Invalidated: false,
            options.Value.SubscriberNumber,
            patientReference,
            recordId,
            new AuditActor(actor.Id, actor.Role),
            new AuditAccess(
                access.Authorized,
                access.TreatmentRelationship,
                access.Consent,
                access.EmergencyAccess),
            new AuditOperation(
                operation,
                purpose,
                AuditActionType.Query,
                AuditDataCategory.PatientIdentification,
                outcome),
            new AuditExchange(
                responseCode,
                durationMilliseconds is null ? null : checked((int)Math.Min(durationMilliseconds.Value, int.MaxValue))));
    }

    private async Task WriteAuditAsync(AuditEntry entry, Guid operationId)
    {
        try
        {
            await auditWriter.WriteAsync(entry, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogAuditWriteFailed(logger, exception, operationId);
            alerts.AuditStorageUnavailable(AuditStorageOperation.Write, operationId);
            throw new AuditUnavailableException(operationId, "Audit storage is unavailable.", exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "SBV-Z request timed out for operation {OperationId}.")]
    private static partial void LogSbvzTimeout(
        ILogger logger,
        Exception exception,
        Guid operationId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "SBV-Z request failed for operation {OperationId}.")]
    private static partial void LogSbvzFailure(
        ILogger logger,
        Exception exception,
        Guid operationId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Audit write failed for operation {OperationId}.")]
    private static partial void LogAuditWriteFailed(
        ILogger logger,
        Exception exception,
        Guid operationId);

    private static SbvzPersonQuery CreateQuery(
        BsnPersonInput person,
        BsnAddressInput? address,
        string? bsn)
    {
        return new SbvzPersonQuery(
            bsn,
            person.GivenNames,
            person.Initial,
            person.SurnamePrefix,
            person.Surname,
            person.BirthDate,
            person.BirthPlace,
            person.BirthCountry,
            ToSbvzSex(person.Sex),
            address is null
                ? null
                : new SbvzAddressQuery(
                    address.Municipality,
                    address.Street,
                    address.HouseNumber,
                    address.HouseLetter,
                    address.HouseNumberSuffix,
                    address.HouseNumberDesignation,
                    address.PostalCode));
    }

    private static string? ToSbvzSex(BsnSex? sex)
    {
        return sex switch
        {
            BsnSex.Male => "M",
            BsnSex.Female => "V",
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(sex), sex, null)
        };
    }

    private static void ValidateContext(
        ApiActor actor,
        ApiAccessContext access,
        string purpose,
        string? recordId)
    {
        RequireObject(actor, "actor");
        RequireObject(access, "access");
        RequireValue(actor.Id, 100, "actor.id");
        RequireValue(actor.Role, 100, "actor.role");
        RequireValue(purpose, 100, "purpose");

        if (recordId is not null)
        {
            RequireValue(recordId, 100, "recordId");
        }
    }

    private static bool HasAccess(ApiAccessContext access)
    {
        return access.EmergencyAccess
            || (access.Authorized
                && access.TreatmentRelationship is not false
                && access.Consent is not false);
    }

    private static void RequireObject(object? value, string field)
    {
        if (value is null)
        {
            throw new SbvzValidationException(field, "Required value is missing.");
        }
    }

    private static void RequireValue(string value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new SbvzValidationException(
                field,
                $"Value must be non-blank, at most {maximumLength} characters, and contain no surrounding or control characters.");
        }
    }

    private static string CreateResponseCode(SbvzQueryResponse response)
    {
        var messageCodes = string.Join(',', response.Messages.Select(message => message.Code));

        return string.IsNullOrEmpty(messageCodes)
            ? ToSbvzResultCode(response.Result)
            : $"{ToSbvzResultCode(response.Result)}:{messageCodes}";
    }

    private static BsnSearchPath ToApiSearchPath(SbvzSearchPath searchPath)
    {
        return searchPath switch
        {
            SbvzSearchPath.Address => BsnSearchPath.Address,
            SbvzSearchPath.Surname => BsnSearchPath.Surname,
            _ => throw new ArgumentOutOfRangeException(nameof(searchPath), searchPath, null)
        };
    }

    private static string ToSbvzResultCode(SbvzResult result)
    {
        return result switch
        {
            SbvzResult.Good => "G",
            SbvzResult.GoodWithDifferences => "A",
            SbvzResult.Error => "F",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    }
}

internal sealed class AuditUnavailableException(
    Guid operationId,
    string message,
    Exception innerException) : Exception(message, innerException)
{
    public Guid OperationId { get; } = operationId;
}

internal sealed class SbvzOperationException(
    Guid operationId,
    SbvzOperationFailure failure,
    string message,
    Exception innerException) : Exception(message, innerException)
{
    public Guid OperationId { get; } = operationId;
    public SbvzOperationFailure Failure { get; } = failure;
}

internal sealed class SbvzAccessDeniedException(Guid operationId) : Exception("Access was refused.")
{
    public Guid OperationId { get; } = operationId;
}

internal enum SbvzOperationFailure
{
    Upstream,
    Timeout
}
