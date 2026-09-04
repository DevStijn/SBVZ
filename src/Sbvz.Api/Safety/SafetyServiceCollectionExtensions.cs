using Microsoft.Extensions.Options;
using Sbvz.Api.Audit;

namespace Sbvz.Api.Safety;

internal static class SafetyServiceCollectionExtensions
{
    public static IServiceCollection AddEmergencyStop(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EmergencyStopOptions>()
            .Configure(options =>
            {
                options.ObjectKey = configuration[EmergencyStopOptions.ObjectKeyVariable]
                    ?? EmergencyStopOptions.DefaultObjectKey;
            })
            .Validate(
                options => IsValidObjectKey(options.ObjectKey),
                $"{EmergencyStopOptions.ObjectKeyVariable} must be a safe S3 object key outside the audit prefix.")
            .Validate<IOptions<S3AuditOptions>>(
                (options, auditOptions) => !options.ObjectKey.StartsWith(
                    $"{auditOptions.Value.Prefix}/",
                    StringComparison.Ordinal),
                $"{EmergencyStopOptions.ObjectKeyVariable} must not be covered by the immutable audit prefix.")
            .ValidateOnStart();

        services.AddSingleton<IEmergencyStopObjectStore, S3EmergencyStopObjectStore>();
        services.AddSingleton<IEmergencyStop, R2EmergencyStop>();

        return services;
    }

    private static bool IsValidObjectKey(string value)
    {
        return value.Length is >= 1 and <= 256
            && value[0] != '/'
            && value[^1] != '/'
            && value.Split('/').All(
                segment => segment.Length > 0
                    && segment is not ("." or "..")
                    && segment.All(
                        character => char.IsAsciiLetterOrDigit(character)
                            || character is '-' or '_' or '.'));
    }
}
