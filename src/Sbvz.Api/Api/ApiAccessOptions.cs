namespace Sbvz.Api.Api;

internal sealed class ApiAccessOptions
{
    public const string AuthenticationScheme = "InternalApi";
    public const string DefaultAuthenticationScheme = "SbvzAuthentication";
    public const string AuthorizationPolicy = "InternalApi";
    public const string ClientIdVariable = "SBVZ_API_CLIENT_ID";
    public const string ApiKeyVariable = "SBVZ_API_KEY";
    public const string ApiKeyFileVariable = "SBVZ_API_KEY_FILE";
    public const string ApiKeySha256Variable = "SBVZ_API_KEY_SHA256";
    public const string ApiKeySha256FileVariable = "SBVZ_API_KEY_SHA256_FILE";
    public const string RateLimitPolicy = "InternalApi";

    public string ClientId { get; set; } = string.Empty;
    public string ApiKeySha256 { get; set; } = string.Empty;
}
