using System.Formats.Asn1;
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
                DateTimeOffset.UtcNow.AddDays(1),
                Enum.Parse<SbvzMode>(mode),
                "12345678");
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
                DateTimeOffset.UtcNow.AddDays(-1),
                SbvzMode.Acceptance,
                "12345678");
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
                DateTimeOffset.UtcNow.AddDays(1),
                SbvzMode.Acceptance,
                "12345678");

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

    [Theory]
    [InlineData(nameof(SbvzMode.Acceptance))]
    [InlineData(nameof(SbvzMode.Production))]
    public void AcceptsPrivateG1CertificateDuringGenerationTransition(string mode)
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
                DateTimeOffset.UtcNow.AddDays(1),
                Enum.Parse<SbvzMode>(mode),
                "12345678",
                useG4Policies: false);
            File.WriteAllText(passwordPath, password);

            using var provider = CreateProvider(mode, certificatePath, passwordPath);

            _ = provider.GetRequiredService<IOptions<SbvzOptions>>().Value;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(2048, "12345678", true)]
    [InlineData(4096, "87654321", true)]
    [InlineData(4096, "12345678", false)]
    public void RejectsCertificateOutsideUziServerProfile(
        int keySize,
        string certificateSubscriberNumber,
        bool includeClientAuthentication)
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
                DateTimeOffset.UtcNow.AddDays(1),
                SbvzMode.Acceptance,
                certificateSubscriberNumber,
                keySize,
                includeClientAuthentication);
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
    public void RejectsCertificateWhoseDnsNameDoesNotMatchCommonName()
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
                DateTimeOffset.UtcNow.AddDays(1),
                SbvzMode.Acceptance,
                "12345678",
                subjectAlternativeName: "other-sbvz.example");
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
        DateTimeOffset notAfter,
        SbvzMode mode,
        string subscriberNumber,
        int keySize = 4096,
        bool includeClientAuthentication = true,
        bool useG4Policies = true,
        string subjectAlternativeName = "test-sbvz.example")
    {
        using var key = RSA.Create(keySize);
        var request = new CertificateRequest(
            "CN=test-sbvz.example,O=Example,C=NL",
            key,
            HashAlgorithmName.SHA512,
            RSASignaturePadding.Pss);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(CreateEnhancedKeyUsage(includeClientAuthentication));
        request.CertificateExtensions.Add(
            CreateSubjectAlternativeName(mode, subscriberNumber, subjectAlternativeName));
        request.CertificateExtensions.Add(CreateCertificatePolicies(mode, useG4Policies));
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);

        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
    }

    private static X509EnhancedKeyUsageExtension CreateEnhancedKeyUsage(
        bool includeClientAuthentication)
    {
        var usages = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1")
        };

        if (includeClientAuthentication)
        {
            usages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        }

        return new X509EnhancedKeyUsageExtension(usages, critical: false);
    }

    private static X509Extension CreateSubjectAlternativeName(
        SbvzMode mode,
        string subscriberNumber,
        string dnsName)
    {
        var subjectIdPrefix = mode switch
        {
            SbvzMode.Acceptance => "2.16.528.1.1007.99.2110",
            SbvzMode.Production => "2.16.528.1.1003.1.3.5.5.5",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        var subjectId = $"{subjectIdPrefix}-1-900000001-S-{subscriberNumber}-00.000-12345678";
        var otherNameTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.WriteCharacterString(
            UniversalTagNumber.IA5String,
            dnsName,
            new Asn1Tag(TagClass.ContextSpecific, 2));
        writer.PushSequence(otherNameTag);
        writer.WriteObjectIdentifier("2.5.5.5");
        writer.PushSequence(otherNameTag);
        writer.WriteCharacterString(UniversalTagNumber.IA5String, subjectId);
        writer.PopSequence(otherNameTag);
        writer.PopSequence(otherNameTag);
        writer.PopSequence();

        return new X509Extension("2.5.29.17", writer.Encode(), critical: false);
    }

    private static X509Extension CreateCertificatePolicies(
        SbvzMode mode,
        bool useG4Policies)
    {
        string[] policyOids = (mode, useG4Policies) switch
        {
            (SbvzMode.Acceptance, true) => ["2.16.528.1.1007.99.44.15.35.11"],
            (SbvzMode.Acceptance, false) => ["2.16.528.1.1007.99.12"],
            (SbvzMode.Production, true) =>
            [
                "2.16.528.1.1003.1.2.44.15.35.11",
                "0.4.0.2042.1.1"
            ],
            (SbvzMode.Production, false) => ["2.16.528.1.1003.1.2.8.6"],
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();

        foreach (var policyOid in policyOids)
        {
            writer.PushSequence();
            writer.WriteObjectIdentifier(policyOid);
            writer.PopSequence();
        }

        writer.PopSequence();

        return new X509Extension("2.5.29.32", writer.Encode(), critical: false);
    }
}
