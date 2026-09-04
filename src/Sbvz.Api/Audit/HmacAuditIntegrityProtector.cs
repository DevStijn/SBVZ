using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Audit;

internal sealed class HmacAuditIntegrityProtector : IAuditIntegrityProtector, IDisposable
{
    private const string Algorithm = "hmac-sha256";
    private static readonly byte[] DerivationContext = Encoding.ASCII.GetBytes(
        "SBVZ audit content integrity v1");
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

    public string Protect(ReadOnlySpan<byte> content)
    {
        var mac = HMACSHA256.HashData(_integrityKey, content);

        return $"{Algorithm}:{_keyId}:{Convert.ToHexStringLower(mac)}";
    }

    public bool Verify(ReadOnlySpan<byte> content, string? integrityValue)
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

        var actual = HMACSHA256.HashData(_integrityKey, content);

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

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_integrityKey);
    }
}
