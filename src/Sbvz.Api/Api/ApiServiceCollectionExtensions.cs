using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Sbvz.Api.Configuration;
using Sbvz.Api.Portal;

namespace Sbvz.Api.Api;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddInternalApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services
            .AddOptions<ApiAccessOptions>()
            .Configure(options =>
            {
                options.ClientId = configuration[ApiAccessOptions.ClientIdVariable] ?? string.Empty;
                options.ApiKeySha256 = SecretValueResolver.Resolve(
                    configuration[ApiAccessOptions.ApiKeySha256Variable],
                    configuration[ApiAccessOptions.ApiKeySha256FileVariable]);

                if (environment.IsDevelopment()
                    && string.IsNullOrWhiteSpace(options.ApiKeySha256))
                {
                    var developmentApiKey = SecretValueResolver.Resolve(
                        configuration[ApiAccessOptions.ApiKeyVariable],
                        configuration[ApiAccessOptions.ApiKeyFileVariable]);

                    if (IsStrongApiKey(developmentApiKey))
                    {
                        options.ApiKeySha256 = Convert.ToHexStringLower(
                            SHA256.HashData(Encoding.UTF8.GetBytes(developmentApiKey)));
                    }
                }
            })
            .Validate(
                options => IsSafeClientId(options.ClientId),
                $"{ApiAccessOptions.ClientIdVariable} must contain a valid client identifier.")
            .Validate(
                options => IsSha256Hash(options.ApiKeySha256),
                $"{ApiAccessOptions.ApiKeySha256Variable} or {ApiAccessOptions.ApiKeySha256FileVariable} must contain a lowercase SHA-256 hash. A raw API key is accepted only in Development.")
            .ValidateOnStart();

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiAccessOptions.DefaultAuthenticationScheme;
                options.DefaultChallengeScheme = ApiAccessOptions.DefaultAuthenticationScheme;
            })
            .AddPolicyScheme(
                ApiAccessOptions.DefaultAuthenticationScheme,
                displayName: null,
                options => options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments("/v1")
                        ? ApiAccessOptions.AuthenticationScheme
                        : AuditPortalConstants.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiAccessOptions.AuthenticationScheme,
                _ => { });
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                ApiAccessOptions.AuthorizationPolicy,
                policy => policy
                    .AddAuthenticationSchemes(ApiAccessOptions.AuthenticationScheme)
                    .RequireAuthenticatedUser());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<BsnOperationService>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                ApiAccessOptions.RateLimitPolicy,
                CreateApiRateLimitPartition);
        });

        return services;
    }

    private static RateLimitPartition<string> CreateApiRateLimitPartition(HttpContext context)
    {
        var clientId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAuthenticatedApiClient = context.User.Identity is
        {
            IsAuthenticated: true,
            AuthenticationType: ApiAccessOptions.AuthenticationScheme
        } && !string.IsNullOrWhiteSpace(clientId);

        return isAuthenticatedApiClient
            ? RateLimitPartition.GetFixedWindowLimiter(
                $"client:{clientId}",
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 60,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                })
            : RateLimitPartition.GetFixedWindowLimiter(
                $"unauthenticated:{context.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 10,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(5)
                });
    }

    private static bool IsSafeClientId(string value)
    {
        return value.Length is >= 1 and <= 100
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
    }

    private static bool IsSha256Hash(string value)
    {
        return value.Length == 64
            && value.All(character => char.IsAsciiDigit(character)
                || character is >= 'a' and <= 'f');
    }

    private static bool IsStrongApiKey(string value)
    {
        byte[]? key = null;

        try
        {
            if (value.Length is < 44 or > 4_096
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            key = Convert.FromBase64String(value);

            return key.Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
    }
}
