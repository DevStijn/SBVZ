using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Tests;

internal static class TestSbvzServices
{
    public static void UseTestClient(
        IServiceCollection services,
        ISbvzClient? client = null)
    {
        services.RemoveAll<IValidateOptions<SbvzOptions>>();
        services.RemoveAll<ISbvzClient>();
        services.AddSingleton(client ?? new FictionalSbvzClient());

        var certificateMonitors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ClientCertificateMonitor))
            .ToArray();

        foreach (var descriptor in certificateMonitors)
        {
            services.Remove(descriptor);
        }
    }
}
