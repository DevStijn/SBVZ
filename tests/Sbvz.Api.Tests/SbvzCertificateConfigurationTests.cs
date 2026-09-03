using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class SbvzCertificateConfigurationTests
{
    [Theory]
    [InlineData(nameof(SbvzMode.Acceptance))]
    [InlineData(nameof(SbvzMode.Production))]
    public void LoadsPkcs12AndPasswordFileForConnectedModes(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("sbvz-certificate-");

        try
        {
            var certificatePath = Path.Combine(directory.FullName, "client.pfx");
            var passwordPath = Path.Combine(directory.FullName, "client-certificate-password");
            const string password = "fictional-password";
            WritePkcs12(
                certificatePath,
                password,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(passwordPath, $"{password}\n");

            using var provider = CreateProvider(mode, certificatePath, passwordPath);
            var options = provider.GetRequiredService<IOptions<SbvzOptions>>().Value;
            using var client = provider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(SbvzConstants.HttpClientName);

            Assert.Equal(password, options.CertificatePassword);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RejectsExpiredPkcs12OutsideMockMode()
    {
        var directory = Directory.CreateTempSubdirectory("sbvz-certificate-");

        try
        {
            var certificatePath = Path.Combine(directory.FullName, "client.pfx");
            var passwordPath = Path.Combine(directory.FullName, "client-certificate-password");
            const string password = "fictional-password";
            WritePkcs12(
                certificatePath,
                password,
                DateTimeOffset.UtcNow.AddDays(-2),
                DateTimeOffset.UtcNow.AddDays(-1));
            File.WriteAllText(passwordPath, password);

            using var provider = CreateProvider(
                nameof(SbvzMode.Acceptance),
                certificatePath,
                passwordPath);

            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<SbvzOptions>>().Value);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RejectsMissingModeInsteadOfFallingBackToMock()
    {
        using var provider = CreateProvider(string.Empty, string.Empty, string.Empty);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SbvzOptions>>().Value);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(SbvzOptions.ModeVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsPasswordlessPkcs12OutsideMockMode()
    {
        var directory = Directory.CreateTempSubdirectory("sbvz-certificate-");

        try
        {
            var certificatePath = Path.Combine(directory.FullName, "client.pfx");
            WritePkcs12(
                certificatePath,
                string.Empty,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddDays(1));

            using var provider = CreateProvider(
                nameof(SbvzMode.Acceptance),
                certificatePath,
                string.Empty);
            var exception = Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<SbvzOptions>>().Value);

            Assert.Contains(
                exception.Failures,
                failure => failure.Contains(
                    SbvzOptions.CertificatePasswordVariable,
                    StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ServiceProvider CreateProvider(
        string mode,
        string certificatePath,
        string passwordPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [SbvzOptions.ModeVariable] = mode,
                    [SbvzOptions.SubscriberNumberVariable] = "12345678",
                    [SbvzOptions.CertificatePathVariable] = certificatePath,
                    [SbvzOptions.CertificatePasswordFileVariable] = passwordPath
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSbvzClient(configuration);

        return services.BuildServiceProvider();
    }

    private static void WritePkcs12(
        string path,
        string password,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=test-sbvz.example",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);

        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
    }
}
