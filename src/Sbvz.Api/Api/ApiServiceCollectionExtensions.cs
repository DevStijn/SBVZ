using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Sbvz.Api.Configuration;

namespace Sbvz.Api.Api;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddInternalApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ApiAccessOptions>()
            .Configure(options =>
            {
                options.ApiKey = SecretValueResolver.Resolve(
                    configuration[ApiAccessOptions.ApiKeyVariable],
                    configuration[ApiAccessOptions.ApiKeyFileVariable]);
            })
            .Validate(
                options => IsStrongApiKey(options.ApiKey),
                $"{ApiAccessOptions.ApiKeyVariable} must be a Base64-encoded key of at least 32 bytes.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<BsnOperationService>();
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                ApiAccessOptions.RateLimitPolicy,
                context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 60,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));
        });

        return services;
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
