using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sbvz.Api.Audit;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class S3AuditWriterTests
{
    [Fact]
    public async Task WritesStableJsonUnderAuditPrefix()
    {
        var store = new RecordingAuditObjectStore();
        using var integrityProtector = CreateIntegrityProtector();
        var writer = new S3AuditWriter(store, integrityProtector, CreateOptions());
        var entry = CreateEntry();

        var receipt = await writer.WriteAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(
            "audit/2026/09/02/01990f73-4963-7c51-a54f-83d482033732/180405123-01990f7349637c51a54f83d482033731.json",
            receipt.ObjectKey);
        Assert.Equal(receipt.ObjectKey, store.ObjectKey);
        Assert.Equal(receipt.ContentIntegrity, store.ContentIntegrity);
        Assert.NotNull(store.Content);
        Assert.True(integrityProtector.Verify(receipt.ObjectKey, store.Content, receipt.ContentIntegrity));

        using var document = JsonDocument.Parse(store.Content);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "01990f73-4963-7c51-a54f-83d482033732",
            root.GetProperty("operationId").GetString());
        Assert.Equal("12345678", root.GetProperty("subscriberNumber").GetString());
        Assert.False(root.TryGetProperty("correlationId", out _));
        Assert.False(root.TryGetProperty("subject", out _));
        Assert.False(root.TryGetProperty("action", out _));
        Assert.False(root.TryGetProperty("accessChecks", out _));
        Assert.False(root.TryGetProperty("cancelled", out _));
        Assert.Equal("succeeded", root.GetProperty("operation").GetProperty("outcome").GetString());
        Assert.Equal(
            $"hmac-sha256:test-v1:{new string('a', 64)}",
            root.GetProperty("patientReference").GetString());
        Assert.DoesNotContain("123456782", Encoding.UTF8.GetString(store.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsPlainBsnBeforeWriting()
    {
        var store = new RecordingAuditObjectStore();
        using var integrityProtector = CreateIntegrityProtector();
        var writer = new S3AuditWriter(store, integrityProtector, CreateOptions());
        var entry = CreateEntry() with
        {
            PatientReference = "123456782"
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(entry, TestContext.Current.CancellationToken));

        Assert.Contains("HMAC-SHA256", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.Content);
    }

    private static AuditEntry CreateEntry()
    {
        return new AuditEntry(
            AuditEntry.CurrentSchemaVersion,
            Guid.Parse("01990f73-4963-7c51-a54f-83d482033731"),
            new DateTimeOffset(2026, 9, 2, 18, 4, 5, 123, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 2, 18, 4, 5, TimeSpan.Zero),
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
                "retrieve-bsn",
                "patient-registration",
                AuditActionType.Query,
                AuditDataCategory.PatientIdentification,
                AuditOutcome.Succeeded),
            new AuditExchange(
                "success",
                125));
    }

    private static IOptions<S3AuditOptions> CreateOptions()
    {
        return Options.Create(
            new S3AuditOptions
            {
                Prefix = "audit"
            });
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

    private sealed class RecordingAuditObjectStore : IAuditObjectStore
    {
        public string? ObjectKey { get; private set; }
        public byte[]? Content { get; private set; }
        public string? ContentIntegrity { get; private set; }

        public Task WriteOnceAsync(
            string objectKey,
            ReadOnlyMemory<byte> content,
            string contentIntegrity,
            CancellationToken cancellationToken)
        {
            ObjectKey = objectKey;
            Content = content.ToArray();
            ContentIntegrity = contentIntegrity;

            return Task.CompletedTask;
        }
    }
}
