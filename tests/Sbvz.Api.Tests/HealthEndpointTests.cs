using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Sbvz.Api.Api;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class HealthEndpointTests(HealthApplicationFactory application)
    : IClassFixture<HealthApplicationFactory>
{
    [Fact]
    public async Task HealthReturnsOk()
    {
        using var client = application.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(health);
        Assert.Equal("ok", health.Status);
    }
}

public sealed class HealthApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SBVZ_MODE"] = "Acceptance",
                    ["SBVZ_SUBSCRIBER_NUMBER"] = "12345678",
                    ["SBVZ_API_CLIENT_ID"] = "test-client",
                    ["SBVZ_API_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    ["SBVZ_API_KEY_SHA256"] = "51643eac9777b63a7b268174d1fd4276daedec9bc9ea0bc6e5abf69047bc54f6",
                    ["SBVZ_AUDIT_S3_BUCKET"] = "fictional-bucket",
                    ["SBVZ_AUDIT_S3_ENDPOINT"] = "https://storage.example",
                    ["SBVZ_AUDIT_S3_REGION"] = "fictional-region",
                    ["SBVZ_AUDIT_S3_PREFIX"] = "audit",
                    ["SBVZ_AUDIT_S3_ACCESS_KEY_ID"] = "fictional-access-key",
                    ["SBVZ_AUDIT_S3_SECRET_ACCESS_KEY"] = "fictional-secret-key",
                    ["SBVZ_AUDIT_PATIENT_REFERENCE_KEY_ID"] = "test-v1",
                    ["SBVZ_AUDIT_PATIENT_REFERENCE_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    ["SBVZ_ALERT_WEBHOOK_URL"] = string.Empty,
                    ["SBVZ_ALERT_WEBHOOK_URL_FILE"] = string.Empty
                });
        });
        builder.ConfigureTestServices(services => TestSbvzServices.UseTestClient(services));
    }
}
