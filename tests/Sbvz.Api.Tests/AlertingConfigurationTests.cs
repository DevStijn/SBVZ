using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class AlertingConfigurationTests
{
    [Fact]
    public void AllowsAlertingToRemainDisabled()
    {
        using var provider = CreateProvider(string.Empty);

        var options = provider.GetRequiredService<IOptions<AlertWebhookOptions>>().Value;

        Assert.False(options.Enabled);
    }

    [Theory]
    [InlineData("http://alerts.example/webhook")]
    [InlineData("https://user:password@alerts.example/webhook")]
    [InlineData("https://alerts.example/webhook#fragment")]
    [InlineData("not-a-url")]
    public void RejectsUnsafeOrInvalidWebhookUrl(string webhookUrl)
    {
        using var provider = CreateProvider(webhookUrl);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<AlertWebhookOptions>>().Value);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                AlertWebhookOptions.WebhookUrlVariable,
                StringComparison.Ordinal));
    }

    private static ServiceProvider CreateProvider(string webhookUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [AlertWebhookOptions.WebhookUrlVariable] = webhookUrl,
                    [AlertWebhookOptions.WebhookUrlFileVariable] = string.Empty
                })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSecurityAlerting(configuration);

        return services.BuildServiceProvider();
    }
}
