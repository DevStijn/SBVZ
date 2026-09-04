using Sbvz.Api.Audit;

namespace Sbvz.Api.Safety;

public interface IEmergencyStop
{
    Task<EmergencyStopStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task ActivateAsync(AuditActor actor, CancellationToken cancellationToken);
}

public enum EmergencyStopStatus
{
    Inactive,
    Active,
    Unavailable
}

internal sealed class EmergencyStopActivationException(
    string message,
    Exception innerException) : Exception(message, innerException);
