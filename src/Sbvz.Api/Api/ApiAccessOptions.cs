namespace Sbvz.Api.Api;

internal sealed class ApiAccessOptions
{
    public const string ApiKeyVariable = "SBVZ_API_KEY";
    public const string ApiKeyFileVariable = "SBVZ_API_KEY_FILE";
    public const string RateLimitPolicy = "InternalApi";

    public string ApiKey { get; set; } = string.Empty;
}
