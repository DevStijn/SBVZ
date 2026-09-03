using Microsoft.Extensions.Options;
using Sbvz.Api.Api;
using Sbvz.Api.Audit;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class BsnOperationServiceTests
{
    [Fact]
    public async Task WritesAttemptBeforeCallAndResultAfterCall()
    {
        var auditWriter = new RecordingAuditWriter();
        var client = new RecordingSbvzClient();
        var service = CreateService(client, auditWriter);
        var request = CreateLookupRequest();

        var response = await service.LookupAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.OperationId);
        Assert.Equal("078211529", response.Answer?.Person?.Bsn);
        Assert.True(client.WasCalled);
        Assert.Equal(response.OperationId.ToString("D"), client.LocalReference);
        Assert.Collection(
            auditWriter.Entries,
            entry =>
            {
                Assert.Equal(response.OperationId.ToString("D"), entry.OperationId);
                Assert.Equal(AuditOutcome.Attempted, entry.Operation.Outcome);
                Assert.Null(entry.PatientReference);
            },
            entry =>
            {
                Assert.Equal(response.OperationId.ToString("D"), entry.OperationId);
                Assert.Equal(AuditOutcome.Succeeded, entry.Operation.Outcome);
                Assert.StartsWith("hmac-sha256:test-v1:", entry.PatientReference, StringComparison.Ordinal);
                Assert.DoesNotContain("078211529", entry.PatientReference, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task GeneratesANewOperationIdForEveryCall()
    {
        var auditWriter = new RecordingAuditWriter();
        var client = new RecordingSbvzClient();
        var service = CreateService(client, auditWriter);
        var request = CreateLookupRequest();

        var first = await service.LookupAsync(request, CancellationToken.None);
        var second = await service.LookupAsync(request, CancellationToken.None);

        Assert.NotEqual(first.OperationId, second.OperationId);
        Assert.Equal(first.OperationId.ToString("D"), auditWriter.Entries[0].OperationId);
        Assert.Equal(first.OperationId.ToString("D"), auditWriter.Entries[1].OperationId);
        Assert.Equal(second.OperationId.ToString("D"), auditWriter.Entries[2].OperationId);
        Assert.Equal(second.OperationId.ToString("D"), auditWriter.Entries[3].OperationId);
    }

    [Fact]
    public async Task DoesNotCallSbvzWhenAttemptAuditFails()
    {
        var auditWriter = new RecordingAuditWriter { FailWrites = true };
        var client = new RecordingSbvzClient();
        var service = CreateService(client, auditWriter);

        await Assert.ThrowsAsync<AuditUnavailableException>(
            () => service.LookupAsync(CreateLookupRequest(), CancellationToken.None));

        Assert.False(client.WasCalled);
    }

    [Fact]
    public async Task RecordsClientCancellationAsCancelled()
    {
        var auditWriter = new RecordingAuditWriter();
        var service = CreateService(new CancelledSbvzClient(), auditWriter);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LookupAsync(CreateLookupRequest(), cancellationSource.Token));

        Assert.Collection(
            auditWriter.Entries,
            entry => Assert.Equal(AuditOutcome.Attempted, entry.Operation.Outcome),
            entry => Assert.Equal(AuditOutcome.Cancelled, entry.Operation.Outcome));
    }

    private static BsnOperationService CreateService(
        ISbvzClient client,
        IAuditWriter auditWriter)
    {
        var referenceOptions = Options.Create(new AuditPatientReferenceOptions
        {
            KeyId = "test-v1",
            Key = Convert.ToBase64String(new byte[32])
        });
        var sbvzOptions = Options.Create(new SbvzOptions
        {
            Mode = nameof(SbvzMode.Mock),
            SubscriberNumber = "12345678"
        });

        return new BsnOperationService(
            client,
            auditWriter,
            new HmacPatientReferenceGenerator(referenceOptions),
            sbvzOptions,
            TimeProvider.System);
    }

    private static BsnLookupRequest CreateLookupRequest()
    {
        return new BsnLookupRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(
                EmergencyAccess: false,
                TreatmentRelationship: true,
                Consent: true),
            Purpose: "patient-registration",
            Person: new BsnPersonInput(
                Surname: "Test-GG-Gevonden",
                BirthDate: "19700101",
                Sex: BsnSex.Male),
            RecordId: "fictional-record");
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];
        public bool FailWrites { get; init; }

        public Task<AuditWriteReceipt> WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new InvalidOperationException("Fictional audit failure.");
            }

            Entries.Add(entry);

            return Task.FromResult(new AuditWriteReceipt("fictional-key", "fictional-hash"));
        }
    }

    private sealed class RecordingSbvzClient : ISbvzClient
    {
        public bool WasCalled { get; private set; }
        public string? LocalReference { get; private set; }

        public Task<SbvzQueryResponse> QueryAsync(
            SbvzPersonQuery query,
            string localReference,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LocalReference = localReference;

            return Task.FromResult(
                new SbvzQueryResponse(
                    localReference,
                    SbvzResult.Good,
                    new SbvzAnswer(
                        new SbvzPersonAnswer(
                            "078211529",
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null),
                        null,
                        null,
                        null),
                    [new SbvzMessage(SbvzMessageType.Good, "23002", "BSN gevonden")]));
        }
    }

    private sealed class CancelledSbvzClient : ISbvzClient
    {
        public Task<SbvzQueryResponse> QueryAsync(
            SbvzPersonQuery query,
            string localReference,
            CancellationToken cancellationToken = default)
        {
            return Task.FromCanceled<SbvzQueryResponse>(cancellationToken);
        }
    }
}
