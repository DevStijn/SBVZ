using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sbvz.Api.Safety;

namespace Sbvz.Api.Health;

internal sealed class EmergencyStopHealthCheck(IEmergencyStop emergencyStop) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = await emergencyStop.GetStatusAsync(cancellationToken);

        return status is EmergencyStopStatus.Inactive
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
    }
}
