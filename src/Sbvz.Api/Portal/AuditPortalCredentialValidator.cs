using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OtpNet;

namespace Sbvz.Api.Portal;

public interface IAuditPortalCredentialValidator
{
    bool Validate(string username, string password, string totpCode);
}

internal sealed class AuditPortalCredentialValidator(
    IOptions<AuditPortalOptions> options,
    IPasswordHasher<AuditPortalUser> passwordHasher) : IAuditPortalCredentialValidator
{
    private readonly Lock _replayLock = new();
    private long _lastAcceptedTimeWindow = -1;

    public bool Validate(string username, string password, string totpCode)
    {
        var configuredUsername = options.Value.Username;
        var user = new AuditPortalUser(configuredUsername);
        var usernameMatches = FixedTimeEquals(username, configuredUsername);
        var passwordResult = VerifyPassword(user, options.Value.PasswordHash, password);
        var totpMatches = VerifyTotp(options.Value.TotpSecret, totpCode, out var timeWindow);

        if (!usernameMatches
            || passwordResult is PasswordVerificationResult.Failed
            || !totpMatches)
        {
            return false;
        }

        lock (_replayLock)
        {
            if (timeWindow <= _lastAcceptedTimeWindow)
            {
                return false;
            }

            _lastAcceptedTimeWindow = timeWindow;
        }

        return true;
    }

    private PasswordVerificationResult VerifyPassword(
        AuditPortalUser user,
        string passwordHash,
        string password)
    {
        try
        {
            return passwordHasher.VerifyHashedPassword(user, passwordHash, password);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private static bool VerifyTotp(string base32Secret, string code, out long timeWindow)
    {
        timeWindow = -1;

        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        byte[]? secret = null;

        try
        {
            secret = Base32Encoding.ToBytes(base32Secret);
            var totp = new Totp(secret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

            return totp.VerifyTotp(
                code,
                out timeWindow,
                new VerificationWindow(previous: 1, future: 1));
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            if (secret is not null)
            {
                CryptographicOperations.ZeroMemory(secret);
            }
        }
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
