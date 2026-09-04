using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OtpNet;

const int passwordHashIterations = 600_000;
const int minimumPasswordLength = 16;

if (args.Length == 0)
{
    WriteUsage();
    return 2;
}

return args[0] switch
{
    "api" => CreateApiCredentials(args[1..]),
    "portal" => CreatePortalCredentials(args[1..]),
    _ => InvalidArguments()
};

static int CreateApiCredentials(string[] arguments)
{
    if (!TryParseArguments(arguments, usernameRequired: false, out var outputDirectory, out _)
        || !ValidateOutputDirectory(outputDirectory))
    {
        return 2;
    }

    var apiKeyPath = Path.Combine(outputDirectory, "api-key");
    var apiKeyHashPath = Path.Combine(outputDirectory, "api-key-sha256");

    if (File.Exists(apiKeyHashPath))
    {
        Console.Error.WriteLine("The API-key hash already exists. It was not overwritten.");
        return 2;
    }

    if (File.Exists(apiKeyPath))
    {
        var existingApiKey = File.ReadAllText(apiKeyPath).Trim();

        if (!IsStrongApiKey(existingApiKey))
        {
            Console.Error.WriteLine("The existing API key is not a Base64-encoded key of at least 32 bytes.");
            return 2;
        }

        WriteApiKeyHash(apiKeyHashPath, existingApiKey);
        Console.WriteLine($"Created {apiKeyHashPath} for the existing API key.");
        return 0;
    }

    var keyBytes = RandomNumberGenerator.GetBytes(32);

    try
    {
        var apiKey = Convert.ToBase64String(keyBytes);
        WriteSecret(apiKeyPath, apiKey);

        try
        {
            WriteApiKeyHash(apiKeyHashPath, apiKey);
        }
        catch
        {
            File.Delete(apiKeyPath);
            throw;
        }

        Console.WriteLine($"Created {apiKeyPath}");
        Console.WriteLine($"Created {apiKeyHashPath}");
        Console.WriteLine("Give api-key only to the calling application; configure the service with api-key-sha256.");
        return 0;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(keyBytes);
    }
}

static int CreatePortalCredentials(string[] arguments)
{
    if (!TryParseArguments(arguments, usernameRequired: true, out var outputDirectory, out var username)
        || !ValidateOutputDirectory(outputDirectory))
    {
        return 2;
    }

    if (username.Length is < 1 or > 100
        || !string.Equals(username, username.Trim(), StringComparison.Ordinal)
        || username.Any(char.IsControl))
    {
        Console.Error.WriteLine("The username must contain between 1 and 100 valid characters.");
        return 2;
    }

    var passwordHashPath = Path.Combine(outputDirectory, "audit-portal-password-hash");
    var totpSecretPath = Path.Combine(outputDirectory, "audit-portal-totp-secret");

    if (FilesExist(passwordHashPath, totpSecretPath))
    {
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
        return 0;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(totpBytes);
    }
}

static int InvalidArguments()
{
    WriteUsage();
    return 2;
}

static void WriteUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project tools/Sbvz.Credentials -- api --output <absolute-directory>");
    Console.Error.WriteLine("  dotnet run --project tools/Sbvz.Credentials -- portal --output <absolute-directory> --username <username>");
}

static bool ValidateOutputDirectory(string outputDirectory)
{
    if (Path.IsPathFullyQualified(outputDirectory) && Directory.Exists(outputDirectory))
    {
        return true;
    }

    Console.Error.WriteLine("The output directory must be an existing absolute directory.");
    return false;
}

static bool FilesExist(params string[] paths)
{
    if (!paths.Any(File.Exists))
    {
        return false;
    }

    Console.Error.WriteLine("Credential files already exist. They were not overwritten.");
    return true;
}

static bool IsStrongApiKey(string value)
{
    byte[]? decoded = null;

    try
    {
        decoded = Convert.FromBase64String(value);
        return decoded.Length >= 32;
    }
    catch (FormatException)
    {
        return false;
    }
    finally
    {
        if (decoded is not null)
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }
}

static void WriteApiKeyHash(string path, string apiKey)
{
    var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

    try
    {
        WriteSecret(path, Convert.ToHexStringLower(hashBytes));
    }
    finally
    {
        CryptographicOperations.ZeroMemory(hashBytes);
    }
}

static bool TryParseArguments(
    string[] arguments,
    bool usernameRequired,
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
            case "--username" when usernameRequired:
                username = arguments[index + 1];
                break;
            default:
                return false;
        }
    }

    return !string.IsNullOrWhiteSpace(outputDirectory)
        && (!usernameRequired || !string.IsNullOrWhiteSpace(username));
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

    try
    {
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(leftHash);
        CryptographicOperations.ZeroMemory(rightHash);
    }
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
    using var writer = new StreamWriter(
        stream,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    writer.WriteLine(value);
}
