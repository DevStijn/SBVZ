using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sbvz.Api.Audit;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class HmacPatientReferenceGeneratorTests
{
    [Fact]
    public void CreatesDeterministicReferenceWithoutExposingBsn()
    {
        var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var options = Options.Create(new AuditPatientReferenceOptions
        {
            KeyId = "test-v1",
            Key = Convert.ToBase64String(key)
        });
        var generator = new HmacPatientReferenceGenerator(options);
        var expectedHash = Convert.ToHexStringLower(
            HMACSHA256.HashData(key, Encoding.ASCII.GetBytes("123456782")));

        var reference = generator.CreateFromBsn("123456782");

        Assert.Equal($"hmac-sha256:test-v1:{expectedHash}", reference);
        Assert.DoesNotContain("123456782", reference, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678")]
    [InlineData("000000000")]
    [InlineData("12345678A")]
    public void RejectsInvalidBsn(string bsn)
    {
        var options = Options.Create(new AuditPatientReferenceOptions
        {
            KeyId = "test-v1",
            Key = Convert.ToBase64String(new byte[32])
        });
        var generator = new HmacPatientReferenceGenerator(options);

        Assert.Throws<ArgumentException>(() => generator.CreateFromBsn(bsn));
    }
}
