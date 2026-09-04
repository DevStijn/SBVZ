using System.Globalization;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Audit;

internal sealed class S3AuditReader(
    IAmazonS3 client,
    IOptions<S3AuditOptions> options,
    IAuditIntegrityProtector integrityProtector) : IAuditReader
{
    private const int MaximumObjectsPerDay = 10_000;
    private const int MaximumObjectBytes = 64 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    public async Task<AuditPage> ReadPageAsync(
        DateOnly auditDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 100);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);
        var keys = await ListKeysAsync(auditDate, timeout.Token);
        var operationGroups = keys
            .Select(key => new
            {
                Key = key,
                OperationId = TryReadOperationId(key, auditDate, out var operationId)
                    ? operationId
                    : null
            })
            .Where(item => item.OperationId is not null)
            .GroupBy(item => item.OperationId!, StringComparer.Ordinal)
            .OrderByDescending(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        var totalCount = operationGroups.Length;
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var effectivePage = Math.Min(page, totalPages);
        var pageGroups = operationGroups
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize)
            .ToArray();
        var entries = new List<AuditOperationRecord>(pageGroups.Length);

        foreach (var group in pageGroups)
        {
            var records = new List<StoredAuditRecord>();

            foreach (var item in group.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                records.Add(await ReadEntryAsync(item.Key, timeout.Token));
            }

            entries.Add(CreateOperationRecord(group.Key, records));
        }

        return new AuditPage(entries, effectivePage, pageSize, totalPages, totalCount);
    }

    private async Task<List<string>> ListKeysAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request
        {
            BucketName = options.Value.Bucket,
            Prefix = string.Create(
                CultureInfo.InvariantCulture,
                $"{options.Value.Prefix}/{date:yyyy/MM/dd}/"),
            MaxKeys = 1_000
        };

        do
        {
            var response = await client.ListObjectsV2Async(request, cancellationToken);

            foreach (var auditObject in response.S3Objects)
            {
                if (!auditObject.Key.EndsWith(".json", StringComparison.Ordinal))
                {
                    continue;
                }

                keys.Add(auditObject.Key);

                if (keys.Count > MaximumObjectsPerDay)
                {
                    throw new AuditStorageException(
                        $"Audit day exceeds the limit of {MaximumObjectsPerDay} objects.");
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (request.ContinuationToken is not null);

        return keys;
    }

    private async Task<StoredAuditRecord> ReadEntryAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetObjectAsync(
            options.Value.Bucket,
            objectKey,
            cancellationToken);
        var content = await ReadBoundedAsync(response.ResponseStream, cancellationToken);
        var expectedIntegrity = response.Metadata["content-integrity"];

        return DeserializeAndValidate(
            objectKey,
            options.Value.Prefix,
            content,
            expectedIntegrity,
            integrityProtector);
    }

    internal static StoredAuditRecord DeserializeAndValidate(
        string objectKey,
        string prefix,
        byte[] content,
        string? expectedIntegrity,
        IAuditIntegrityProtector integrityProtector)
    {
        if (!integrityProtector.Verify(objectKey, content, expectedIntegrity))
        {
            throw new AuditStorageException($"Audit object '{objectKey}' failed its integrity check.");
        }

        try
        {
            return ReadCurrentEntry(objectKey, prefix, content);
        }
        catch (JsonException exception)
        {
            throw new AuditStorageException(
                $"Audit object '{objectKey}' contains invalid JSON.",
                exception);
        }
        catch (ArgumentException exception)
        {
            throw new AuditStorageException(
                $"Audit object '{objectKey}' contains an invalid entry.",
                exception);
        }
    }

    private static StoredAuditRecord ReadCurrentEntry(
        string objectKey,
        string prefix,
        byte[] content)
    {
        var entry = JsonSerializer.Deserialize<AuditEntry>(content, AuditJson.SerializerOptions)
            ?? throw new AuditStorageException($"Audit object '{objectKey}' is empty.");

        if (entry.SchemaVersion != AuditEntry.CurrentSchemaVersion)
        {
            throw new AuditStorageException(
                $"Audit object '{objectKey}' uses unsupported schema version {entry.SchemaVersion}.");
        }

        AuditEntryValidator.Validate(entry);

        if (!string.Equals(
                objectKey,
                S3AuditWriter.CreateObjectKey(entry, prefix),
                StringComparison.Ordinal))
        {
            throw new AuditStorageException(
                $"Audit object '{objectKey}' does not match its recorded identity.");
        }

        return new StoredAuditRecord(
            entry.SchemaVersion,
            entry.EventId,
            entry.RegisteredAtUtc,
            entry.OperationStartedAtUtc,
            entry.OperationId,
            entry.TraceId,
            entry.Invalidated,
            entry.SubscriberNumber,
            entry.PatientReference,
            entry.RecordId,
            entry.ApiClientId,
            entry.Actor.Id,
            entry.Actor.Role,
            entry.Access.Authorized,
            entry.Access.TreatmentRelationship,
            entry.Access.Consent,
            entry.Access.EmergencyAccess,
            entry.Operation.Name,
            entry.Operation.Purpose,
            entry.Operation.ActionType,
            entry.Operation.DataCategory,
            entry.Operation.Outcome,
            entry.Exchange.ResponseCode,
            entry.Exchange.DurationMilliseconds);
    }

    private bool TryReadOperationId(
        string objectKey,
        DateOnly date,
        out string? operationId)
    {
        var prefix = string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Value.Prefix}/{date:yyyy/MM/dd}/");
        operationId = null;

        if (!objectKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = objectKey.AsSpan(prefix.Length);
        var separator = remainder.IndexOf('/');

        if (separator <= 0
            || remainder[(separator + 1)..].Contains('/')
            || !Guid.TryParseExact(remainder[..separator], "D", out var parsed))
        {
            return false;
        }

        operationId = parsed.ToString("D");

        return true;
    }

    private static AuditOperationRecord CreateOperationRecord(
        string operationId,
        List<StoredAuditRecord> records)
    {
        if (records.Count is < 1 or > 2)
        {
            throw new AuditStorageException(
                $"Audit operation '{operationId}' contains an invalid number of events.");
        }

        records.Sort((left, right) => left.RegisteredAtUtc.CompareTo(right.RegisteredAtUtc));
        var attempted = records[0];

        if (attempted.Outcome is not AuditOutcome.Attempted)
        {
            throw new AuditStorageException(
                $"Audit operation '{operationId}' does not start with an attempted event.");
        }

        var completion = records.Count == 2 ? records[1] : null;

        if (completion?.Outcome is AuditOutcome.Attempted)
        {
            throw new AuditStorageException(
                $"Audit operation '{operationId}' contains two attempted events.");
        }

        if (completion is not null)
        {
            ValidateMatchingOperation(attempted, completion);
        }

        var effective = completion ?? attempted;

        return new AuditOperationRecord(
            effective.SchemaVersion,
            attempted.EventId,
            completion?.EventId,
            effective.OperationStartedAtUtc,
            completion?.RegisteredAtUtc,
            effective.OperationId,
            effective.TraceId,
            effective.Invalidated,
            effective.SubscriberNumber,
            effective.PatientReference ?? attempted.PatientReference,
            effective.RecordId,
            effective.ApiClientId,
            effective.ActorId,
            effective.ActorRole,
            effective.Authorized,
            effective.TreatmentRelationship,
            effective.Consent,
            effective.EmergencyAccess,
            effective.OperationName,
            effective.Purpose,
            effective.ActionType,
            effective.DataCategory,
            effective.Outcome,
            effective.ResponseCode,
            effective.DurationMilliseconds);
    }

    private static void ValidateMatchingOperation(
        StoredAuditRecord attempted,
        StoredAuditRecord completion)
    {
        var actorMatches = attempted.ActorId == completion.ActorId
            || (attempted.OperationName == "portal-login" && attempted.ActorId == "anonymous");

        if (attempted.OperationId != completion.OperationId
            || attempted.OperationStartedAtUtc != completion.OperationStartedAtUtc
            || attempted.TraceId != completion.TraceId
            || attempted.SubscriberNumber != completion.SubscriberNumber
            || attempted.RecordId != completion.RecordId
            || attempted.ApiClientId != completion.ApiClientId
            || !actorMatches
            || attempted.ActorRole != completion.ActorRole
            || attempted.Authorized != completion.Authorized
            || attempted.TreatmentRelationship != completion.TreatmentRelationship
            || attempted.Consent != completion.Consent
            || attempted.EmergencyAccess != completion.EmergencyAccess
            || attempted.OperationName != completion.OperationName
            || attempted.Purpose != completion.Purpose
            || attempted.ActionType != completion.ActionType
            || attempted.DataCategory != completion.DataCategory
            || (attempted.PatientReference is not null
                && attempted.PatientReference != completion.PatientReference))
        {
            throw new AuditStorageException(
                $"Audit operation '{attempted.OperationId}' contains inconsistent events.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        var buffer = new byte[8 * 1024];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);

            if (bytesRead == 0)
            {
                return content.ToArray();
            }

            if (content.Length + bytesRead > MaximumObjectBytes)
            {
                throw new AuditStorageException(
                    $"Audit object exceeds the limit of {MaximumObjectBytes} bytes.");
            }

            await content.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

}

internal sealed record StoredAuditRecord(
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
