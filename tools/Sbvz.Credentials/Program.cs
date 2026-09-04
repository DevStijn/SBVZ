using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OtpNet;

const int passwordHashIterations = 600_000;
const int minimumPasswordLength = 16;
const string passwordHashFileName = "audit-portal-password-hash";
const string totpSecretFileName = "audit-portal-totp-secret";

if (!TryParseArguments(args, out var outputDirectory, out var username))
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tools/Sbvz.Credentials -- --output <absolute-directory> --username <username>");
    return 2;
}

if (!Path.IsPathFullyQualified(outputDirectory) || !Directory.Exists(outputDirectory))
{
    Console.Error.WriteLine("The output directory must be an existing absolute directory.");
    return 2;
}

if (username.Length is < 1 or > 100
    || !string.Equals(username, username.Trim(), StringComparison.Ordinal)
    || username.Any(char.IsControl))
{
    Console.Error.WriteLine("The username must contain between 1 and 100 valid characters.");
    return 2;
}

var passwordHashPath = Path.Combine(outputDirectory, passwordHashFileName);
var totpSecretPath = Path.Combine(outputDirectory, totpSecretFileName);

if (File.Exists(passwordHashPath) || File.Exists(totpSecretPath))
{
    Console.Error.WriteLine("Credential files already exist. They were not overwritten.");
    return 2;
}

var password = ReadPassword("Password: ");

if (password.Length < minimumPasswordLength)
{
    Console.Error.WriteLine($"The password must contain at least {minimumPasswordLength} characters.");
    return 2;
}

var confirmation = ReadPassword("Repeat password: ");

if (!FixedTimeEquals(password, confirmation))
{
    Console.Error.WriteLine("The passwords do not match.");
    return 2;
}

var hasher = new PasswordHasher<string>(
    Options.Create(
        new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = passwordHashIterations
        }));
var passwordHash = hasher.HashPassword(username, password);
var totpBytes = RandomNumberGenerator.GetBytes(32);

try
{
    var totpSecret = Base32Encoding.ToString(totpBytes);
    WriteSecret(passwordHashPath, passwordHash);

    try
    {
        WriteSecret(totpSecretPath, totpSecret);
    }
    catch
    {
        File.Delete(passwordHashPath);
        throw;
    }

    var issuer = Uri.EscapeDataString("SBV-Z");
    var account = Uri.EscapeDataString(username);
    var encodedSecret = Uri.EscapeDataString(totpSecret);

    Console.WriteLine();
    Console.WriteLine($"Created {passwordHashPath}");
    Console.WriteLine($"Created {totpSecretPath}");
    Console.WriteLine();
    Console.WriteLine("Add this account to the authenticator:");
    Console.WriteLine($"otpauth://totp/{issuer}:{account}?secret={encodedSecret}&issuer={issuer}&digits=6&period=30");
}
finally
{
    CryptographicOperations.ZeroMemory(totpBytes);
}

return 0;

static bool TryParseArguments(
    string[] arguments,
    out string outputDirectory,
    out string username)
{
    outputDirectory = string.Empty;
    username = string.Empty;

    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
        {
            return false;
        }

        switch (arguments[index])
        {
            case "--output":
                outputDirectory = arguments[index + 1];
                break;
            case "--username":
                username = arguments[index + 1];
                break;
            default:
                return false;
        }
    }

    return !string.IsNullOrWhiteSpace(outputDirectory)
        && !string.IsNullOrWhiteSpace(username);
}

static string ReadPassword(string prompt)
{
    if (Console.IsInputRedirected)
    {
        throw new InvalidOperationException("Password input must use an interactive terminal.");
    }

    Console.Write(prompt);
    var password = new StringBuilder();

    while (true)
    {
        var key = Console.ReadKey(intercept: true);

        if (key.Key is ConsoleKey.Enter)
        {
            Console.WriteLine();
            return password.ToString();
        }

        if (key.Key is ConsoleKey.Backspace)
        {
            if (password.Length > 0)
            {
                password.Length--;
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar) && password.Length < 1_024)
        {
            password.Append(key.KeyChar);
        }
    }
}

static bool FixedTimeEquals(string left, string right)
{
    var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
    var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));

    return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
}

static void WriteSecret(string path, string value)
{
    var options = new FileStreamOptions
    {
        Access = FileAccess.Write,
        Mode = FileMode.CreateNew,
        Share = FileShare.None
    };

    if (!OperatingSystem.IsWindows())
    {
        options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    }

    using var stream = new FileStream(path, options);
    using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine(value);
}
