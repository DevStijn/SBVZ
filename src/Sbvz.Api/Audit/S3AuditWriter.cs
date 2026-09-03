using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sbvz.Api.Audit;

internal sealed class S3AuditWriter(IAuditObjectStore objectStore) : IAuditWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public async Task<AuditWriteReceipt> WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        Validate(entry);

        var content = JsonSerializer.SerializeToUtf8Bytes(entry, SerializerOptions);
        var contentSha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var objectKey = CreateObjectKey(entry);

        await objectStore.WriteOnceAsync(
            objectKey,
            content,
            contentSha256,
            cancellationToken);

        return new AuditWriteReceipt(objectKey, contentSha256);
    }

    internal static string CreateObjectKey(AuditEntry entry)
    {
        var timestamp = entry.RegisteredAtUtc.UtcDateTime;

        return $"audit/{timestamp:yyyy/MM/dd}/{timestamp:HHmmssfff}-{entry.EventId:N}.json";
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    private static void Validate(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Actor);
        ArgumentNullException.ThrowIfNull(entry.Access);
        ArgumentNullException.ThrowIfNull(entry.Operation);
        ArgumentNullException.ThrowIfNull(entry.Exchange);

        if (entry.SchemaVersion != AuditEntry.CurrentSchemaVersion)
        {
            throw new ArgumentException("Unsupported audit schema version.", nameof(entry));
        }

        if (entry.EventId == Guid.Empty)
        {
            throw new ArgumentException("Event ID must be set.", nameof(entry));
        }

        if (entry.RegisteredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Registration time must use UTC.", nameof(entry));
        }

        RequireValue(entry.OperationId, nameof(entry.OperationId));
        RequireValue(entry.SubscriberNumber, nameof(entry.SubscriberNumber));
        RequireOptionalValue(entry.RecordId, nameof(entry.RecordId));
        RequireValue(entry.Operation.Name, nameof(entry.Operation.Name));
        RequireValue(entry.Operation.Purpose, nameof(entry.Operation.Purpose));
        RequireValue(entry.Actor.Id, nameof(entry.Actor.Id));
        RequireValue(entry.Actor.Role, nameof(entry.Actor.Role));

        if (entry.PatientReference is not null
            && !entry.PatientReference.StartsWith("hmac-sha256:", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Patient reference must be generated with HMAC-SHA256.",
                nameof(entry));
        }

        if (entry.Exchange.DurationMilliseconds < 0)
        {
            throw new ArgumentException("Duration cannot be negative.", nameof(entry));
        }
    }

    private static void RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} must be set.", name);
        }
    }

    private static void RequireOptionalValue(string? value, string name)
    {
        if (value is not null)
        {
            RequireValue(value, name);
        }
    }
}
