using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Health;

internal sealed class SbvzCertificateHealthCheck(
    IOptions<SbvzOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configured = options.Value;

            if (!Enum.TryParse<SbvzMode>(configured.Mode, ignoreCase: true, out var mode))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy());
            }

            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                configured.CertificatePath,
                configured.CertificatePassword,
                OperatingSystem.IsMacOS()
                    ? X509KeyStorageFlags.DefaultKeySet
                    : X509KeyStorageFlags.EphemeralKeySet);
            var now = timeProvider.GetUtcNow();
            var valid = certificate.NotBefore.ToUniversalTime() <= now
                && certificate.NotAfter.ToUniversalTime() > now
                && UziServerCertificateValidator.IsValid(
                    certificate,
                    mode,
                    configured.SubscriberNumber);

            return Task.FromResult(
                valid ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
        }
        catch (Exception exception) when (exception is CryptographicException
            or IOException
            or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }
    }
}
