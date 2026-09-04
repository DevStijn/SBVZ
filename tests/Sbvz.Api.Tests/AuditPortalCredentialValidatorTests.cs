using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OtpNet;
using Sbvz.Api.Portal;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class AuditPortalCredentialValidatorTests
{
    private const string Username = "admin";
    private const string Password = "fictional-password-for-tests";

    [Fact]
    public void AcceptsValidCredentialsOnlyOncePerTotpWindow()
    {
        var secretBytes = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var secret = Base32Encoding.ToString(secretBytes);
        var hasher = CreateHasher();
        var options = Options.Create(
            new AuditPortalOptions
            {
                Enabled = true,
                Username = Username,
                PasswordHash = hasher.HashPassword(new AuditPortalUser(Username), Password),
                TotpSecret = secret
            });
        var validator = new AuditPortalCredentialValidator(options, hasher);
        var code = new Totp(secretBytes).ComputeTotp();

        Assert.True(validator.Validate(Username, Password, code));
        Assert.False(validator.Validate(Username, Password, code));
    }

    [Theory]
    [InlineData("other-user", Password, "123456")]
    [InlineData(Username, "wrong-password", "123456")]
    [InlineData(Username, Password, "12345")]
    [InlineData(Username, Password, "abcdef")]
    public void RejectsInvalidCredentials(string username, string password, string totpCode)
    {
        var secretBytes = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var hasher = CreateHasher();
        var options = Options.Create(
            new AuditPortalOptions
            {
                Enabled = true,
                Username = Username,
                PasswordHash = hasher.HashPassword(new AuditPortalUser(Username), Password),
                TotpSecret = Base32Encoding.ToString(secretBytes)
            });
        var validator = new AuditPortalCredentialValidator(options, hasher);

        Assert.False(validator.Validate(username, password, totpCode));
    }

    private static PasswordHasher<AuditPortalUser> CreateHasher()
    {
        return new PasswordHasher<AuditPortalUser>(
            Options.Create(
                new PasswordHasherOptions
                {
                    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                    IterationCount = 600_000
                }));
    }
}
