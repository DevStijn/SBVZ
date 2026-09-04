using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Audit;

internal sealed class S3AuditWriter(
    IAuditObjectStore objectStore,
    IAuditIntegrityProtector integrityProtector,
    IOptions<S3AuditOptions> options) : IAuditWriter
{
    public async Task<AuditWriteReceipt> WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        AuditEntryValidator.Validate(entry);

        var content = JsonSerializer.SerializeToUtf8Bytes(entry, AuditJson.SerializerOptions);
        var objectKey = CreateObjectKey(entry, options.Value.Prefix);
        var contentIntegrity = integrityProtector.Protect(objectKey, content);

        await objectStore.WriteOnceAsync(
            objectKey,
            content,
            contentIntegrity,
            cancellationToken);

        return new AuditWriteReceipt(objectKey, contentIntegrity);
    }

    internal static string CreateObjectKey(AuditEntry entry, string prefix)
    {
        var operationDate = entry.OperationStartedAtUtc.UtcDateTime;
        var eventTimestamp = entry.RegisteredAtUtc.UtcDateTime;

        return $"{prefix}/{operationDate:yyyy/MM/dd}/{entry.OperationId}/{eventTimestamp:HHmmssfff}-{entry.EventId:N}.json";
    }

}
