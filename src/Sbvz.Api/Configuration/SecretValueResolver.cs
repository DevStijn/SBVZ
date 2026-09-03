namespace Sbvz.Api.Configuration;

internal static class SecretValueResolver
{
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

        return File.ReadAllText(filePath).Trim();
    }
}
