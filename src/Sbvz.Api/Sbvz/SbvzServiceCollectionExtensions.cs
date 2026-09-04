using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Sbvz.Api.Configuration;

namespace Sbvz.Api.Sbvz;

public static class SbvzServiceCollectionExtensions
{
    public static IServiceCollection AddSbvzClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SbvzOptions>()
            .Configure(options =>
            {
                options.Mode = configuration[SbvzOptions.ModeVariable] ?? string.Empty;
                options.SubscriberNumber = NormalizeSubscriberNumber(
                    configuration[SbvzOptions.SubscriberNumberVariable] ?? string.Empty);
                options.CertificatePath = configuration[SbvzOptions.CertificatePathVariable] ?? string.Empty;
                options.CertificatePassword = SecretValueResolver.Resolve(
                    configuration[SbvzOptions.CertificatePasswordVariable],
                    configuration[SbvzOptions.CertificatePasswordFileVariable]);

                var configuredTimeout = configuration[SbvzOptions.TimeoutSecondsVariable];

                if (!string.IsNullOrWhiteSpace(configuredTimeout))
                {
                    options.TimeoutSeconds = int.TryParse(configuredTimeout, out var timeoutSeconds)
                        ? timeoutSeconds
                        : 0;
                }
            })
            .Validate(
                options => Enum.TryParse<SbvzMode>(options.Mode, ignoreCase: true, out _),
                $"{SbvzOptions.ModeVariable} must be Acceptance or Production.")
            .Validate(
                options => options.SubscriberNumber.Length == 8
                    && options.SubscriberNumber.All(char.IsAsciiDigit),
                $"{SbvzOptions.SubscriberNumberVariable} must contain one to eight digits.")
            .Validate(
                IsCertificateConfigurationValid,
                $"{SbvzOptions.CertificatePathVariable} must point to a valid UZI PKCS#12 file for the configured environment.")
            .Validate(
                HasCertificatePassword,
                $"{SbvzOptions.CertificatePasswordVariable} or {SbvzOptions.CertificatePasswordFileVariable} must be set.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 120,
                $"{SbvzOptions.TimeoutSecondsVariable} must be between 1 and 120.")
            .ValidateOnStart();

        services
            .AddHttpClient(
                SbvzConstants.HttpClientName,
                (provider, client) =>
                {
                    var options = provider.GetRequiredService<IOptions<SbvzOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var options = provider.GetRequiredService<IOptions<SbvzOptions>>().Value;
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CheckCertificateRevocationList = true,
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    UseCookies = false
                };

                var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                    options.CertificatePath,
                    options.CertificatePassword,
                    GetCertificateKeyStorageFlags());

                if (!certificate.HasPrivateKey)
                {
                    certificate.Dispose();
                    throw new InvalidOperationException("SBV-Z client certificate has no private key.");
                }

                handler.ClientCertificates.Add(certificate);

                return handler;
            });

        services.AddSingleton<ISbvzClient, SbvzXmlClient>();

        return services;
    }

    private static bool IsCertificateConfigurationValid(SbvzOptions options)
    {
        if (!Enum.TryParse<SbvzMode>(options.Mode, ignoreCase: true, out var mode))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.CertificatePath)
            || !Path.IsPathFullyQualified(options.CertificatePath)
            || !File.Exists(options.CertificatePath)
            || Path.GetExtension(options.CertificatePath).ToLowerInvariant() is not (".p12" or ".pfx"))
        {
            return false;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                options.CertificatePath,
                options.CertificatePassword,
                GetCertificateKeyStorageFlags());
            var now = DateTimeOffset.UtcNow;

            return certificate.HasPrivateKey
                && certificate.NotBefore.ToUniversalTime() <= now
                && certificate.NotAfter.ToUniversalTime() > now
                && UziServerCertificateValidator.IsValid(
                    certificate,
                    mode,
                    options.SubscriberNumber);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasCertificatePassword(SbvzOptions options)
    {
        return !Enum.TryParse<SbvzMode>(options.Mode, ignoreCase: true, out _)
            || !string.IsNullOrWhiteSpace(options.CertificatePassword);
    }

    private static X509KeyStorageFlags GetCertificateKeyStorageFlags()
    {
        return OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;
    }

    private static string NormalizeSubscriberNumber(string value)
    {
        return value.Length is > 0 and <= 8 && value.All(char.IsAsciiDigit)
            ? value.PadLeft(8, '0')
            : value;
    }
}
