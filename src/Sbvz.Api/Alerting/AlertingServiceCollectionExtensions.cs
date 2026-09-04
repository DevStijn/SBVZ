using Microsoft.Extensions.DependencyInjection.Extensions;
using Sbvz.Api.Configuration;

namespace Sbvz.Api.Alerting;

public static class AlertingServiceCollectionExtensions
{
    private static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(5);

    public static IServiceCollection AddSecurityAlerting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AlertWebhookOptions>()
            .Configure(options =>
            {
                options.WebhookUrl = SecretValueResolver.Resolve(
                    configuration[AlertWebhookOptions.WebhookUrlVariable],
                    configuration[AlertWebhookOptions.WebhookUrlFileVariable]);
            })
            .Validate(
                options => IsValidOptionalWebhookUrl(options.WebhookUrl),
                $"{AlertWebhookOptions.WebhookUrlVariable} or {AlertWebhookOptions.WebhookUrlFileVariable} must contain a valid HTTPS URL.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AlertQueue>();
        services.AddSingleton<IAlertQueue>(provider => provider.GetRequiredService<AlertQueue>());
        services.AddSingleton<ISecurityAlertService, SecurityAlertService>();
        services
            .AddHttpClient<WebhookAlertSender>(client => client.Timeout = WebhookTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            })
            .RemoveAllLoggers();
        services.AddHostedService<WebhookAlertWorker>();
        services.AddHostedService<ClientCertificateMonitor>();

        return services;
    }

    private static bool IsValidOptionalWebhookUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Length <= 2_048
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
