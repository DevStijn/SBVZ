using System.Text.Json;
using Microsoft.Extensions.Options;
using Sbvz.Api.Audit;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class S3AuditReaderTests
{
    [Fact]
    public void ReadsAuditEntryWithMatchingIntegrityValue()
    {
        using var integrityProtector = CreateIntegrityProtector();
        var entry = CreateEntry();
        var objectKey = CreateObjectKey(entry);
        var content = JsonSerializer.SerializeToUtf8Bytes(entry, AuditJson.SerializerOptions);
        var contentIntegrity = integrityProtector.Protect(objectKey, content);

        var result = S3AuditReader.DeserializeAndValidate(
            objectKey,
            "audit",
            content,
            contentIntegrity,
            integrityProtector);

        Assert.Equal(entry.SchemaVersion, result.SchemaVersion);
        Assert.Equal(entry.EventId, result.EventId);
        Assert.Equal(entry.OperationId, result.OperationId);
        Assert.Equal(entry.Actor.Id, result.ActorId);
        Assert.Equal(entry.Operation.Name, result.OperationName);
        Assert.Equal(entry.Operation.Outcome, result.Outcome);
    }

    [Fact]
    public void RejectsAuditEntryWhoseContentWasChanged()
    {
        using var integrityProtector = CreateIntegrityProtector();
        var entry = CreateEntry();
        var objectKey = CreateObjectKey(entry);
        var content = JsonSerializer.SerializeToUtf8Bytes(entry, AuditJson.SerializerOptions);
        var originalIntegrity = integrityProtector.Protect(objectKey, content);
        content[^1] ^= 1;

        var exception = Assert.Throws<AuditStorageException>(
            () => S3AuditReader.DeserializeAndValidate(
                objectKey,
                "audit",
                content,
                originalIntegrity,
                integrityProtector));

        Assert.Contains("integrity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAuditEntryWithoutIntegrityValue()
    {
        using var integrityProtector = CreateIntegrityProtector();
        var entry = CreateEntry();
        var objectKey = CreateObjectKey(entry);
        var content = JsonSerializer.SerializeToUtf8Bytes(
            entry,
            AuditJson.SerializerOptions);

        var exception = Assert.Throws<AuditStorageException>(
            () => S3AuditReader.DeserializeAndValidate(
                objectKey,
                "audit",
                content,
                expectedIntegrity: null,
                integrityProtector: integrityProtector));

        Assert.Contains("integrity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsOldSchemaVersion()
    {
        using var integrityProtector = CreateIntegrityProtector();
        var entry = CreateEntry() with { SchemaVersion = 2 };
        var objectKey = CreateObjectKey(entry);
        var content = JsonSerializer.SerializeToUtf8Bytes(entry, AuditJson.SerializerOptions);
        var contentIntegrity = integrityProtector.Protect(objectKey, content);

        var exception = Assert.Throws<AuditStorageException>(
            () => S3AuditReader.DeserializeAndValidate(
                objectKey,
                "audit",
                content,
                contentIntegrity,
                integrityProtector));

        Assert.Contains("unsupported schema version 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateAuditJsonPropertyEvenWithValidIntegrity()
    {
        using var integrityProtector = CreateIntegrityProtector();
        var entry = CreateEntry();
        var objectKey = CreateObjectKey(entry);
        var original = JsonSerializer.Serialize(entry, AuditJson.SerializerOptions);
        var content = System.Text.Encoding.UTF8.GetBytes(
            original.Insert(1, "\"schemaVersion\":1,"));
        var contentIntegrity = integrityProtector.Protect(objectKey, content);

        var exception = Assert.Throws<AuditStorageException>(
            () => S3AuditReader.DeserializeAndValidate(
                objectKey,
                "audit",
                content,
                contentIntegrity,
                integrityProtector));

        Assert.Contains("invalid JSON", exception.Message, StringComparison.Ordinal);
    }

    private static AuditEntry CreateEntry()
    {
        return new AuditEntry(
            AuditEntry.CurrentSchemaVersion,
            Guid.Parse("01990f73-4963-7c51-a54f-83d482033731"),
            new DateTimeOffset(2026, 9, 3, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 3, 9, 29, 59, TimeSpan.Zero),
            "01990f73-4963-7c51-a54f-83d482033732",
            "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            Invalidated: false,
            "12345678",
            $"hmac-sha256:test-v1:{new string('a', 64)}",
            "fictional-record",
            "test-client",
            new AuditActor("fictional-user", "employee"),
            new AuditAccess(true, true, true, false),
            new AuditOperation(
                "lookup-bsn",
                "patient-registration",
                AuditActionType.Query,
                AuditDataCategory.PatientIdentification,
                AuditOutcome.Succeeded),
            new AuditExchange("success", 125));
    }

    [Fact]
    public void RejectsAuditEntryCopiedToAnotherObjectKey()
    {
        using var integrityProtector = CreateIntegrityProtector();
        var entry = CreateEntry();
        var originalKey = CreateObjectKey(entry);
        var copiedKey = originalKey.Replace("/2026/09/03/", "/2026/09/04/", StringComparison.Ordinal);
        var content = JsonSerializer.SerializeToUtf8Bytes(entry, AuditJson.SerializerOptions);
        var contentIntegrity = integrityProtector.Protect(originalKey, content);

        var exception = Assert.Throws<AuditStorageException>(
            () => S3AuditReader.DeserializeAndValidate(
                copiedKey,
                "audit",
                content,
                contentIntegrity,
                integrityProtector));

        Assert.Contains("integrity", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateObjectKey(AuditEntry entry)
    {
        return S3AuditWriter.CreateObjectKey(entry, "audit");
    }

    private static HmacAuditIntegrityProtector CreateIntegrityProtector()
    {
        return new HmacAuditIntegrityProtector(
            Options.Create(
                new AuditPatientReferenceOptions
                {
                    KeyId = "test-v1",
                    Key = Convert.ToBase64String(new byte[32])
                }));
    }
}
