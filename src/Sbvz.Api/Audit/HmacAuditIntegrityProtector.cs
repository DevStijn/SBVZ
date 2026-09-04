using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Audit;

internal sealed class HmacAuditIntegrityProtector : IAuditIntegrityProtector, IDisposable
{
    private const string Algorithm = "hmac-sha256";
    private static readonly byte[] DerivationContext = Encoding.ASCII.GetBytes(
        "SBVZ audit object integrity v2");
    private readonly string _keyId;
    private readonly byte[] _integrityKey;

    public HmacAuditIntegrityProtector(IOptions<AuditPatientReferenceOptions> options)
    {
        _keyId = options.Value.KeyId;
        var masterKey = Convert.FromBase64String(options.Value.Key);

        try
        {
            _integrityKey = HMACSHA256.HashData(masterKey, DerivationContext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public string Protect(string objectKey, ReadOnlySpan<byte> content)
    {
        var mac = ComputeMac(objectKey, content);

        try
        {
            return $"{Algorithm}:{_keyId}:{Convert.ToHexStringLower(mac)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    public bool Verify(string objectKey, ReadOnlySpan<byte> content, string? integrityValue)
    {
        var prefix = $"{Algorithm}:{_keyId}:";

        if (integrityValue is null
            || !integrityValue.StartsWith(prefix, StringComparison.Ordinal)
            || integrityValue.Length != prefix.Length + 64)
        {
            return false;
        }

        byte[] expected;

        try
        {
            expected = Convert.FromHexString(integrityValue[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = ComputeMac(objectKey, content);

        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private byte[] ComputeMac(string objectKey, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var objectKeyBytes = Encoding.UTF8.GetBytes(objectKey);

        try
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _integrityKey);
            hmac.AppendData(objectKeyBytes);
            hmac.AppendData([0]);
            hmac.AppendData(content);
            return hmac.GetHashAndReset();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(objectKeyBytes);
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_integrityKey);
    }
}
