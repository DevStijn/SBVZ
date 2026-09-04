using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Sbvz.Api.Configuration;

namespace Sbvz.Api.Audit;

public static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddAuditLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<S3AuditOptions>()
            .Configure(options =>
            {
                options.Bucket = configuration[S3AuditOptions.BucketVariable] ?? string.Empty;
                options.Endpoint = configuration[S3AuditOptions.EndpointVariable] ?? string.Empty;
                options.Region = configuration[S3AuditOptions.RegionVariable] ?? string.Empty;
                options.Prefix = configuration[S3AuditOptions.PrefixVariable] ?? string.Empty;
                options.AccessKeyId = SecretValueResolver.Resolve(
                    configuration[S3AuditOptions.AccessKeyIdVariable],
                    configuration[S3AuditOptions.AccessKeyIdFileVariable]);
                options.SecretAccessKey = SecretValueResolver.Resolve(
                    configuration[S3AuditOptions.SecretAccessKeyVariable],
                    configuration[S3AuditOptions.SecretAccessKeyFileVariable]);
            })
            .Validate(
                options => IsSafeConfigurationValue(options.Bucket, 255),
                $"{S3AuditOptions.BucketVariable} must contain a valid bucket name.")
            .Validate(
                options => IsHttpsEndpoint(options.Endpoint),
                $"{S3AuditOptions.EndpointVariable} must be a valid HTTPS URL.")
            .Validate(
                options => IsSafeConfigurationValue(options.Region, 100),
                $"{S3AuditOptions.RegionVariable} must contain a valid region.")
            .Validate(
                options => IsValidPrefix(options.Prefix),
                $"{S3AuditOptions.PrefixVariable} must contain valid path segments.")
            .Validate(
                options => IsSafeConfigurationValue(options.AccessKeyId, 1_024),
                $"{S3AuditOptions.AccessKeyIdVariable} or {S3AuditOptions.AccessKeyIdFileVariable} must be set.")
            .Validate(
                options => IsSafeConfigurationValue(options.SecretAccessKey, 4_096),
                $"{S3AuditOptions.SecretAccessKeyVariable} or {S3AuditOptions.SecretAccessKeyFileVariable} must be set.")
            .ValidateOnStart();

        services
            .AddOptions<AuditPatientReferenceOptions>()
            .Configure(options =>
            {
                options.KeyId = configuration[AuditPatientReferenceOptions.KeyIdVariable] ?? string.Empty;
                options.Key = SecretValueResolver.Resolve(
                    configuration[AuditPatientReferenceOptions.KeyVariable],
                    configuration[AuditPatientReferenceOptions.KeyFileVariable]);
            })
            .Validate(
                options => IsValidKeyId(options.KeyId),
                $"{AuditPatientReferenceOptions.KeyIdVariable} must contain only letters, numbers, dots, underscores or hyphens.")
            .Validate(
                options => IsStrongBase64Key(options.Key),
                $"{AuditPatientReferenceOptions.KeyVariable} or {AuditPatientReferenceOptions.KeyFileVariable} must contain a Base64-encoded key of at least 32 bytes.")
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<S3AuditOptions>>().Value;
            var credentials = new BasicAWSCredentials(
                options.AccessKeyId,
                options.SecretAccessKey);
            var clientConfiguration = new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                AuthenticationRegion = options.Region
            };

            return new AmazonS3Client(credentials, clientConfiguration);
        });
        services.AddSingleton<IAuditObjectStore, S3AuditObjectStore>();
        services.AddSingleton<IAuditIntegrityProtector, HmacAuditIntegrityProtector>();
        services.AddSingleton<IAuditWriter, S3AuditWriter>();
        services.AddSingleton<IAuditReader, S3AuditReader>();
        services.AddSingleton<IPatientReferenceGenerator, HmacPatientReferenceGenerator>();

        return services;
    }

    private static bool IsHttpsEndpoint(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsValidKeyId(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
    }

    private static bool IsValidPrefix(string value)
    {
        return value.Length is >= 1 and <= 128
            && value[0] != '/'
            && value[^1] != '/'
            && value.Split('/').All(
                segment => segment.Length > 0
                    && segment is not ("." or "..")
                    && segment.All(
                        character => char.IsAsciiLetterOrDigit(character)
                            || character is '-' or '_'));
    }

    private static bool IsSafeConfigurationValue(string value, int maximumLength)
    {
        return value.Length is >= 1
            && value.Length <= maximumLength
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && !value.Any(char.IsControl);
    }

    private static bool IsStrongBase64Key(string value)
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
