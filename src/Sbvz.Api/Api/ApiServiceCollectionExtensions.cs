using System.Security.Cryptography;
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

        return services;
    }

    private static bool IsStrongApiKey(string value)
    {
        byte[]? key = null;

        try
        {
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
