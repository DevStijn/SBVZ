namespace Sbvz.Api.Configuration;

internal static class SecretValueResolver
{
    private const int MaximumSecretFileBytes = 64 * 1024;

    public static string Resolve(string? directValue, string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(directValue))
        {
            return directValue;
        }

        if (string.IsNullOrWhiteSpace(filePath)
            || !Path.IsPathFullyQualified(filePath)
            || !File.Exists(filePath))
        {
            return string.Empty;
        }

        if (new FileInfo(filePath).Length > MaximumSecretFileBytes)
        {
            return string.Empty;
        }

        return File.ReadAllText(filePath).Trim();
    }
}
