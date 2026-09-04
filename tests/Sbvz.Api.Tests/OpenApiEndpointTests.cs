using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class OpenApiEndpointTests
{
    [Fact]
    public async Task DevelopmentExposesDocumentedAuthenticatedOperationsAndScalar()
    {
        using var application = new OpenApiApplicationFactory("Development");
        using var client = application.CreateClient();

        var documentResponse = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var documentJson = await documentResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        documentResponse.EnsureSuccessStatusCode();
        Assert.DoesNotContain(OpenApiApplicationFactory.ApiKey, documentJson, StringComparison.Ordinal);
        Assert.Contains("\"operationId\"", documentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"requestId\"", documentJson, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(documentJson);
        var root = document.RootElement;
        var bearer = root
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");
        var lookup = root
            .GetProperty("paths")
            .GetProperty("/v1/bsn/lookup")
            .GetProperty("post");

        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("Retrieve a BSN", lookup.GetProperty("summary").GetString());
        Assert.Equal(
            "Bearer",
            lookup.GetProperty("security")[0].EnumerateObject().Single().Name);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var lookupRequest = schemas.GetProperty("BsnLookupRequest");
        var lookupRequired = lookupRequest
            .GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToArray();
        var operationResponseProperties = schemas
            .GetProperty("BsnOperationResponse")
            .GetProperty("properties");

        Assert.Contains("actor", lookupRequired);
        Assert.Contains("access", lookupRequired);
        Assert.Contains("purpose", lookupRequired);
        Assert.Contains("person", lookupRequired);
        Assert.DoesNotContain("recordId", lookupRequired);
        Assert.DoesNotContain("address", lookupRequired);
        Assert.False(schemas.GetProperty("BsnPersonInput").TryGetProperty("required", out _));
        Assert.False(schemas.GetProperty("BsnAddressInput").TryGetProperty("required", out _));

        var verifyRequired = schemas
            .GetProperty("BsnVerifyRequest")
            .GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToArray();

        Assert.Contains("bsn", verifyRequired);
        Assert.Contains("person", verifyRequired);
        Assert.True(operationResponseProperties.TryGetProperty("answer", out _));
        Assert.True(operationResponseProperties.TryGetProperty("result", out _));
        Assert.False(operationResponseProperties.TryGetProperty("status", out _));
        Assert.False(operationResponseProperties.TryGetProperty("bsn", out _));
        Assert.False(operationResponseProperties.TryGetProperty("deviatingFields", out _));
        Assert.Equal(
            ["address", "surname"],
            schemas
                .GetProperty("BsnSearchPath")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["M", "V", null],
            schemas
                .GetProperty("BsnSex")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["G", "A", "F"],
            schemas
                .GetProperty("SbvzResult")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        AssertSchemaProperties(
            schemas,
            "SbvzPersonAnswer",
            [
                "birthCountry",
                "birthDate",
                "birthPlace",
                "bsn",
                "givenNames",
                "initial",
                "investigations",
                "nobleTitleOrPredicate",
                "sex",
                "surname",
                "surnamePrefix"
            ]);
        AssertSchemaProperties(
            schemas,
            "SbvzAddressAnswer",
            [
                "addressFunction",
                "countryFromWhichRegistered",
                "foreignAddress",
                "houseLetter",
                "houseNumber",
                "houseNumberDesignation",
                "houseNumberSuffix",
                "investigations",
                "locationDescription",
                "municipality",
                "municipalityPart",
                "placeOfResidence",
                "postalCode",
                "street"
            ]);
        AssertSchemaProperties(
            schemas,
            "SbvzRegistrationAnswer",
            ["disclosureRestriction", "suspensionReason"]);
        AssertSchemaProperties(schemas, "SbvzDeathAnswer", ["date", "investigations"]);
        AssertSchemaProperties(
            schemas,
            "SbvzForeignAddress",
            ["country", "line1", "line2", "line3", "startDate"]);

        var scalarResponse = await client.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);
        var scalarHtml = await scalarResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        scalarResponse.EnsureSuccessStatusCode();
        Assert.Contains("SBV-Z API", scalarHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(OpenApiApplicationFactory.ApiKey, scalarHtml, StringComparison.Ordinal);
    }

    private static void AssertSchemaProperties(
        JsonElement schemas,
        string schemaName,
        IReadOnlyList<string> expectedProperties)
    {
        var actualProperties = schemas
            .GetProperty(schemaName)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedProperties.Order(StringComparer.Ordinal), actualProperties);
    }

    [Fact]
    public async Task ProductionDoesNotExposeOpenApiOrScalar()
    {
        using var application = new OpenApiApplicationFactory("Production");
        using var client = application.CreateClient();

        var documentResponse = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var scalarResponse = await client.GetAsync("/scalar/v1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scalarResponse.StatusCode);
    }

    private sealed class OpenApiApplicationFactory(string environment)
        : WebApplicationFactory<Program>
    {
        public const string ApiKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["SBVZ_MODE"] = "Mock",
                        ["SBVZ_SUBSCRIBER_NUMBER"] = "12345678",
                        ["SBVZ_API_KEY"] = ApiKey,
                        ["SBVZ_AUDIT_S3_BUCKET"] = "fictional-bucket",
                        ["SBVZ_AUDIT_S3_ENDPOINT"] = "https://storage.example",
                        ["SBVZ_AUDIT_S3_REGION"] = "fictional-region",
                        ["SBVZ_AUDIT_S3_PREFIX"] = "audit",
                        ["SBVZ_AUDIT_S3_ACCESS_KEY_ID"] = "fictional-access-key",
                        ["SBVZ_AUDIT_S3_SECRET_ACCESS_KEY"] = "fictional-secret-key",
                        ["SBVZ_AUDIT_PATIENT_REFERENCE_KEY_ID"] = "test-v1",
                        ["SBVZ_AUDIT_PATIENT_REFERENCE_KEY"] = ApiKey,
                        ["SBVZ_ALLOWED_HOSTS"] = "localhost",
                        ["SBVZ_ALERT_WEBHOOK_URL"] = string.Empty,
                        ["SBVZ_ALERT_WEBHOOK_URL_FILE"] = string.Empty
                    });
            });
        }
    }
}
