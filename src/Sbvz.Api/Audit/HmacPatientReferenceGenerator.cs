using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Audit;

internal sealed class HmacPatientReferenceGenerator(
    IOptions<AuditPatientReferenceOptions> options) : IPatientReferenceGenerator, IDisposable
{
    private readonly string keyId = options.Value.KeyId;
    private readonly byte[] key = Convert.FromBase64String(options.Value.Key);

    public string CreateFromBsn(string bsn)
    {
        ValidateBsn(bsn);

        var value = Encoding.ASCII.GetBytes(bsn);

        try
        {
            var hash = HMACSHA256.HashData(key, value);

            return $"hmac-sha256:{keyId}:{Convert.ToHexStringLower(hash)}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(key);
    }

    private static void ValidateBsn(string bsn)
    {
        ArgumentNullException.ThrowIfNull(bsn);

        if (bsn.Length != 9 || !bsn.All(char.IsAsciiDigit) || bsn.All(character => character == '0'))
        {
            throw new ArgumentException("BSN must contain exactly nine digits.", nameof(bsn));
        }

        var checksum = 0;

        for (var index = 0; index < 8; index++)
        {
            checksum += (bsn[index] - '0') * (9 - index);
        }

        checksum -= bsn[8] - '0';

        if (checksum % 11 != 0)
        {
            throw new ArgumentException("BSN failed the eleven test.", nameof(bsn));
        }
    }
}
