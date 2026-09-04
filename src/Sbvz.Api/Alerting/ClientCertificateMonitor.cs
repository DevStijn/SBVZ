using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Alerting;

internal sealed class ClientCertificateMonitor(
    IOptions<SbvzOptions> options,
    ISecurityAlertService alerts,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enum.TryParse<SbvzMode>(options.Value.Mode, ignoreCase: true, out _))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            CheckCertificate();
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private void CheckCertificate()
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                options.Value.CertificatePath,
                options.Value.CertificatePassword,
                GetCertificateKeyStorageFlags());
            var expiresAtUtc = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            var level = DetermineAlertLevel(timeProvider.GetUtcNow(), expiresAtUtc);

            if (level is not null)
            {
                alerts.ClientCertificateAlert(level.Value, expiresAtUtc);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or IOException)
        {
            alerts.ClientCertificateAlert(CertificateAlertLevel.Unavailable);
        }
    }

    internal static CertificateAlertLevel? DetermineAlertLevel(
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - now;

        if (remaining <= TimeSpan.Zero)
        {
            return CertificateAlertLevel.Expired;
        }

        if (remaining <= TimeSpan.FromDays(7))
        {
            return CertificateAlertLevel.ExpiringWithinSevenDays;
        }

        return remaining <= TimeSpan.FromDays(30)
            ? CertificateAlertLevel.ExpiringWithinThirtyDays
            : null;
    }

    private static X509KeyStorageFlags GetCertificateKeyStorageFlags()
    {
        return OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;
    }
}
