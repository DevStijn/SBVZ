using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sbvz.Api.Audit;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class S3AuditWriterTests
{
    [Fact]
    public async Task WritesStableJsonUnderAuditPrefix()
    {
        var store = new RecordingAuditObjectStore();
        var writer = new S3AuditWriter(store);
        var entry = CreateEntry();

        var receipt = await writer.WriteAsync(entry);

        Assert.Equal(
            "audit/2026/09/02/180405123-01990f7349637c51a54f83d482033731.json",
            receipt.ObjectKey);
        Assert.Equal(receipt.ObjectKey, store.ObjectKey);
        Assert.Equal(receipt.ContentSha256, store.ContentSha256);
        Assert.NotNull(store.Content);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(store.Content)),
            receipt.ContentSha256);

        using var document = JsonDocument.Parse(store.Content);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "fictional-operation-id",
            root.GetProperty("operationId").GetString());
        Assert.Equal("fictional-provider", root.GetProperty("subscriberNumber").GetString());
        Assert.False(root.TryGetProperty("correlationId", out _));
        Assert.False(root.TryGetProperty("subject", out _));
        Assert.False(root.TryGetProperty("action", out _));
        Assert.False(root.TryGetProperty("accessChecks", out _));
        Assert.False(root.TryGetProperty("cancelled", out _));
        Assert.Equal("succeeded", root.GetProperty("operation").GetProperty("outcome").GetString());
        Assert.Equal(
            "hmac-sha256:test-v1:fictional-subject",
            root.GetProperty("patientReference").GetString());
        Assert.DoesNotContain("123456782", Encoding.UTF8.GetString(store.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsPlainBsnBeforeWriting()
    {
        var store = new RecordingAuditObjectStore();
        var writer = new S3AuditWriter(store);
        var entry = CreateEntry() with
        {
            PatientReference = "123456782"
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(entry));

        Assert.Contains("HMAC-SHA256", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.Content);
    }

    private static AuditEntry CreateEntry()
    {
        return new AuditEntry(
            AuditEntry.CurrentSchemaVersion,
            Guid.Parse("01990f73-4963-7c51-a54f-83d482033731"),
            new DateTimeOffset(2026, 9, 2, 18, 4, 5, 123, TimeSpan.Zero),
            "fictional-operation-id",
            "fictional-provider",
            "hmac-sha256:test-v1:fictional-subject",
            "fictional-record",
            new AuditActor("fictional-user", "employee"),
            new AuditAccess(true, true, false),
            new AuditOperation(
                "retrieve-bsn",
                "patient-registration",
                AuditOutcome.Succeeded),
            new AuditExchange(
                "success",
                125));
    }

    private sealed class RecordingAuditObjectStore : IAuditObjectStore
    {
        public string? ObjectKey { get; private set; }
        public byte[]? Content { get; private set; }
        public string? ContentSha256 { get; private set; }

        public Task WriteOnceAsync(
            string objectKey,
            ReadOnlyMemory<byte> content,
            string contentSha256,
            CancellationToken cancellationToken)
        {
            ObjectKey = objectKey;
            Content = content.ToArray();
            ContentSha256 = contentSha256;

            return Task.CompletedTask;
        }
    }
}
