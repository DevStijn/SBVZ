using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sbvz.Api.Sbvz;

internal static class UziServerCertificateValidator
{
    private const string CommonNameOid = "2.5.4.3";
    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const string CertificatePoliciesOid = "2.5.29.32";
    private const string UziSubjectIdOtherNameOid = "2.5.5.5";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string ProductionSubjectIdPrefix = "2.16.528.1.1003.1.3.5.5.5";
    private const string AcceptanceSubjectIdPrefix = "2.16.528.1.1007.99.2110";
    private const string ProductionServerPolicyOid = "2.16.528.1.1003.1.2.44.15.35.11";
    private const string ProductionNcpPolicyOid = "0.4.0.2042.1.1";
    private const string AcceptanceServerPolicyOid = "2.16.528.1.1007.99.44.15.35.11";
    private const string ProductionPrivateG1PolicyOid = "2.16.528.1.1003.1.2.8.6";
    private const string AcceptancePrivateG1PolicyOid = "2.16.528.1.1007.99.12";

    public static bool IsValid(
        X509Certificate2 certificate,
        SbvzMode mode,
        string subscriberNumber)
    {
        if (!certificate.HasPrivateKey
            || certificate.GetRSAPublicKey() is not { KeySize: 4096 }
            || certificate.GetRSAPrivateKey() is not { KeySize: 4096 })
        {
            return false;
        }

        if (!HasValidBasicConstraints(certificate)
            || !HasValidKeyUsage(certificate)
            || !HasValidExtendedKeyUsage(certificate))
        {
            return false;
        }

        if (!TryReadSubjectAlternativeNames(
                certificate,
                out var dnsNames,
                out var subjectIds)
            || dnsNames.Count != 1
            || !IsFullyQualifiedDnsName(dnsNames[0])
            || !TryReadSubjectCommonName(certificate, out var commonName)
            || !string.Equals(commonName, dnsNames[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedSubjectIdPrefix = mode switch
        {
            SbvzMode.Acceptance => AcceptanceSubjectIdPrefix,
            SbvzMode.Production => ProductionSubjectIdPrefix,
            _ => null
        };

        if (expectedSubjectIdPrefix is null
            || !subjectIds.Any(subjectId => IsExpectedSubjectId(
                subjectId,
                expectedSubjectIdPrefix,
                subscriberNumber)))
        {
            return false;
        }

        if (!TryReadCertificatePolicies(certificate, out var policies))
        {
            return false;
        }

        return mode switch
        {
            SbvzMode.Acceptance => policies.Contains(AcceptanceServerPolicyOid)
                || policies.Contains(AcceptancePrivateG1PolicyOid),
            SbvzMode.Production => policies.Contains(ProductionPrivateG1PolicyOid)
                || (policies.Contains(ProductionServerPolicyOid)
                    && policies.Contains(ProductionNcpPolicyOid)),
            _ => false
        };
    }

    private static bool HasValidBasicConstraints(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .SingleOrDefault();

        return extension is { Critical: true, CertificateAuthority: false };
    }

    private static bool HasValidKeyUsage(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions
            .OfType<X509KeyUsageExtension>()
            .SingleOrDefault();
        const X509KeyUsageFlags required = X509KeyUsageFlags.DigitalSignature
            | X509KeyUsageFlags.KeyEncipherment;
        const X509KeyUsageFlags forbidden = X509KeyUsageFlags.KeyCertSign
            | X509KeyUsageFlags.CrlSign;

        return extension is { Critical: true }
            && (extension.KeyUsages & required) == required
            && (extension.KeyUsages & forbidden) == 0;
    }

    private static bool HasValidExtendedKeyUsage(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SingleOrDefault();

        if (extension is null)
        {
            return false;
        }

        var usages = extension.EnhancedKeyUsages
            .Cast<Oid>()
            .Select(usage => usage.Value)
            .ToHashSet(StringComparer.Ordinal);

        return usages.Contains(ClientAuthenticationOid)
            && usages.Contains(ServerAuthenticationOid);
    }

    private static bool TryReadSubjectAlternativeNames(
        X509Certificate2 certificate,
        out List<string> dnsNames,
        out List<string> subjectIds)
    {
        dnsNames = [];
        subjectIds = [];
        var extension = certificate.Extensions
            .SingleOrDefault(item => item.Oid?.Value == SubjectAlternativeNameOid);

        if (extension is null)
        {
            return false;
        }

        try
        {
            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            var names = reader.ReadSequence();

            while (names.HasData)
            {
                var tag = names.PeekTag();

                if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
                {
                    dnsNames.Add(
                        names.ReadCharacterString(
                            UniversalTagNumber.IA5String,
                            new Asn1Tag(TagClass.ContextSpecific, 2)));
                    continue;
                }

                if (tag.HasSameClassAndValue(
                        new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    ReadOtherName(names, subjectIds);
                    continue;
                }

                _ = names.ReadEncodedValue();
            }

            reader.ThrowIfNotEmpty();

            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static bool TryReadSubjectCommonName(
        X509Certificate2 certificate,
        out string commonName)
    {
        commonName = string.Empty;
        var commonNames = certificate.SubjectName
            .EnumerateRelativeDistinguishedNames(false)
            .Where(name => !name.HasMultipleElements
                && name.GetSingleElementType().Value == CommonNameOid)
            .Select(name => name.GetSingleElementValue())
            .OfType<string>()
            .ToArray();

        if (commonNames.Length != 1 || string.IsNullOrWhiteSpace(commonNames[0]))
        {
            return false;
        }

        commonName = commonNames[0];
        return true;
    }

    private static void ReadOtherName(AsnReader names, List<string> subjectIds)
    {
        var otherName = names.ReadSequence(
            new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        var typeId = otherName.ReadObjectIdentifier();
        var value = otherName.ReadSequence(
            new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));

        if (typeId == UziSubjectIdOtherNameOid
            && value.PeekTag().HasSameClassAndValue(
                new Asn1Tag(UniversalTagNumber.IA5String)))
        {
            subjectIds.Add(value.ReadCharacterString(UniversalTagNumber.IA5String));
        }
        else
        {
            _ = value.ReadEncodedValue();
        }

        value.ThrowIfNotEmpty();
        otherName.ThrowIfNotEmpty();
    }

    private static bool TryReadCertificatePolicies(
        X509Certificate2 certificate,
        out HashSet<string> policies)
    {
        policies = new HashSet<string>(StringComparer.Ordinal);
        var extension = certificate.Extensions
            .SingleOrDefault(item => item.Oid?.Value == CertificatePoliciesOid);

        if (extension is null)
        {
            return false;
        }

        try
        {
            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            var policyInformation = reader.ReadSequence();

            while (policyInformation.HasData)
            {
                var policy = policyInformation.ReadSequence();
                policies.Add(policy.ReadObjectIdentifier());

                while (policy.HasData)
                {
                    _ = policy.ReadEncodedValue();
                }
            }

            reader.ThrowIfNotEmpty();

            return true;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static bool IsExpectedSubjectId(
        string value,
        string expectedPrefix,
        string subscriberNumber)
    {
        var fields = value.Split('-', StringSplitOptions.None);

        return fields.Length == 7
            && fields[0] == expectedPrefix
            && fields[1] == "1"
            && fields[2].Length == 9
            && fields[2].All(char.IsAsciiDigit)
            && fields[3] == "S"
            && fields[4] == subscriberNumber
            && fields[5] == "00.000"
            && fields[6].Length == 8
            && fields[6].All(char.IsAsciiDigit);
    }

    private static bool IsFullyQualifiedDnsName(string value)
    {
        return value.Length is > 0 and <= 253
            && value.Contains('.', StringComparison.Ordinal)
            && !IPAddress.TryParse(value, out _)
            && Uri.CheckHostName(value) is UriHostNameType.Dns;
    }
}
