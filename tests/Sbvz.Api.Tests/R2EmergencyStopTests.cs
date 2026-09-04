using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Audit;
using Sbvz.Api.Safety;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class R2EmergencyStopTests
{
    [Fact]
    public async Task ReadsActiveMarkerFromObjectStorage()
    {
        var store = new RecordingEmergencyStopObjectStore { Exists = true };
        var writer = new RecordingEmergencyStopAuditWriter();
        var alerts = new RecordingSecurityAlertService();
        using var emergencyStop = CreateEmergencyStop(store, writer, alerts);

        var status = await emergencyStop.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EmergencyStopStatus.Active, status);
        Assert.Equal(1, store.ExistsCalls);
        Assert.Collection(
            writer.Entries,
            entry => Assert.Equal(AuditOutcome.Attempted, entry.Operation.Outcome),
            entry =>
            {
                Assert.Equal(AuditOutcome.Succeeded, entry.Operation.Outcome);
                Assert.Equal("active", entry.Exchange.ResponseCode);
            });
        Assert.Equal(
            EmergencyStopStatus.Active,
            Assert.Single(alerts.EmergencyStopChanges));
    }

    [Fact]
    public async Task PortalActivationCreatesMarkerAndAuditsAdministrator()
    {
        var store = new RecordingEmergencyStopObjectStore();
        var writer = new RecordingEmergencyStopAuditWriter();
        var alerts = new RecordingSecurityAlertService();
        using var emergencyStop = CreateEmergencyStop(store, writer, alerts);
        var actor = new AuditActor("admin", "portal-administrator");

        await emergencyStop.ActivateAsync(actor, TestContext.Current.CancellationToken);
        var status = await emergencyStop.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, store.CreateCalls);
        Assert.Equal(0, store.ExistsCalls);
        Assert.Equal(EmergencyStopStatus.Active, status);
        Assert.Collection(
            writer.Entries,
            entry =>
            {
                Assert.Equal(actor, entry.Actor);
                Assert.Equal(AuditOutcome.Attempted, entry.Operation.Outcome);
            },
            entry =>
            {
                Assert.Equal(actor, entry.Actor);
                Assert.Equal(AuditOutcome.Succeeded, entry.Operation.Outcome);
                Assert.Equal("active", entry.Exchange.ResponseCode);
            });
        Assert.Equal(
            EmergencyStopStatus.Active,
            Assert.Single(alerts.EmergencyStopChanges));
    }

    private static R2EmergencyStop CreateEmergencyStop(
        IEmergencyStopObjectStore store,
        IAuditWriter writer,
        ISecurityAlertService alerts)
    {
        return new R2EmergencyStop(
            store,
            Options.Create(
                new EmergencyStopOptions
                {
                    ObjectKey = "_control/sbvz-disabled"
                }),
            Options.Create(
                new SbvzOptions
                {
                    Mode = nameof(SbvzMode.Acceptance),
                    SubscriberNumber = "12345678"
                }),
            writer,
            alerts,
            TimeProvider.System,
            NullLogger<R2EmergencyStop>.Instance);
    }

    private sealed class RecordingEmergencyStopObjectStore : IEmergencyStopObjectStore
    {
        public bool Exists { get; init; }

        public int ExistsCalls { get; private set; }

        public int CreateCalls { get; private set; }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            ExistsCalls++;

            return Task.FromResult(Exists);
        }

        public Task CreateIfMissingAsync(CancellationToken cancellationToken)
        {
            CreateCalls++;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEmergencyStopAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task<AuditWriteReceipt> WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);

            return Task.FromResult(
                new AuditWriteReceipt("fictional-key", "fictional-integrity"));
        }
    }
}
