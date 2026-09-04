using Sbvz.Api.Audit;
using Sbvz.Api.Safety;

namespace Sbvz.Api.Tests;

internal sealed class RecordingEmergencyStop(
    EmergencyStopStatus initialStatus = EmergencyStopStatus.Inactive) : IEmergencyStop
{
    public EmergencyStopStatus Status { get; private set; } = initialStatus;

    public List<AuditActor> Activations { get; } = [];

    public Task<EmergencyStopStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Status);
    }

    public Task ActivateAsync(AuditActor actor, CancellationToken cancellationToken)
    {
        Activations.Add(actor);
        Status = EmergencyStopStatus.Active;

        return Task.CompletedTask;
    }
}
