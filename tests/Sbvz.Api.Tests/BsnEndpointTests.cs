using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sbvz.Api.Alerting;
using Sbvz.Api.Api;
using Sbvz.Api.Audit;
using Sbvz.Api.Safety;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class BsnEndpointTests
{
    private const string ApiKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public async Task LookupUsesAuthenticatedJsonApiWithoutCaching()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = new BsnLookupRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(
                Authorized: true,
                EmergencyAccess: false,
                TreatmentRelationship: true,
                Consent: true),
            Purpose: "patient-registration",
            Person: new BsnPersonInput(
                Surname: "Test-GG-Gevonden",
                BirthDate: "19700101",
                Sex: BsnSex.Male),
            RecordId: "fictional-record");

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", request, cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(cancellationToken: TestContext.Current.CancellationToken);
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.NotNull(body);
        Assert.Equal(BsnSearchPath.Surname, body.SearchPath);
        Assert.Equal(SbvzResult.Good, body.Result);
        Assert.Equal("surname", responseJson.RootElement.GetProperty("searchPath").GetString());
        Assert.False(responseJson.RootElement.TryGetProperty("status", out _));
        Assert.Equal("G", responseJson.RootElement.GetProperty("result").GetString());
        Assert.Equal(
            "G",
            responseJson.RootElement.GetProperty("messages")[0].GetProperty("type").GetString());
        Assert.Equal("078211529", body.Answer?.Person?.Bsn);
        Assert.NotEqual(Guid.Empty, body.OperationId);
        Assert.All(
            application.AuditWriter.Entries,
            entry => Assert.Equal(body.OperationId.ToString("D"), entry.OperationId));
        Assert.Equal(2, application.AuditWriter.Entries.Count);
    }

    [Fact]
    public async Task LookupRejectsMissingBearerTokenBeforeAuditedOperation()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();

        var response = await client.PostAsync("/v1/bsn/lookup", new StringContent("{", Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ApiKey, responseBody, StringComparison.Ordinal);
        Assert.Empty(application.AuditWriter.Entries);
        Assert.Equal(
            AuthenticationSurface.InternalApi,
            Assert.Single(application.Alerts.AuthenticationFailures));
    }

    [Fact]
    public async Task LookupRejectsUnsupportedContentTypeBeforeAuditing()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        var response = await client.PostAsync("/v1/bsn/lookup", new StringContent("{}", Encoding.UTF8, "text/plain"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task LookupRejectsCallerSuppliedRequestId()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = new
        {
            requestId = Guid.Parse("01990f73-4963-7c51-a54f-83d482033731"),
            actor = new { id = "fictional-user", role = "employee" },
            access = new
            {
                authorized = true,
                treatmentRelationship = true,
                consent = true,
                emergencyAccess = false
            },
            purpose = "patient-registration",
            recordId = "fictional-record",
            person = new
            {
                surname = "Test-GG-Gevonden",
                birthDate = "19700101",
                sex = "M"
            },
            address = (object?)null
        };

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", request, cancellationToken: TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("System.Text.Json", responseBody, StringComparison.Ordinal);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task LookupRejectsDuplicateJsonPropertiesBeforeAuditing()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        const string json = """
            {
              "actor": { "id": "fictional-user", "role": "employee" },
              "access": { "authorized": true, "emergencyAccess": false },
              "purpose": "patient-registration",
              "purpose": "other-purpose",
              "person": {
                "surname": "Test-GG-Gevonden",
                "birthDate": "19700101",
                "sex": "M"
              }
            }
            """;

        var response = await client.PostAsync("/v1/bsn/lookup", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task LookupAcceptsMinimalSurnameSearchPathWithOmittedOptionalProperties()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        const string json = """
            {
              "actor": { "id": "fictional-user", "role": "employee" },
              "access": { "authorized": true, "emergencyAccess": false },
              "purpose": "patient-registration",
              "person": {
                "surname": "Test-GG-Gevonden",
                "birthDate": "19700101",
                "sex": "M"
              }
            }
            """;

        var response = await client.PostAsync("/v1/bsn/lookup", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(BsnSearchPath.Surname, body?.SearchPath);
        Assert.Equal("078211529", body?.Answer?.Person?.Bsn);
    }

    [Fact]
    public async Task LookupAcceptsAddressSearchPathWithoutSurname()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = new BsnLookupRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(Authorized: true, EmergencyAccess: false),
            Purpose: "patient-registration",
            Person: new BsnPersonInput(BirthDate: "19700101", Sex: BsnSex.Male),
            Address: new BsnAddressInput(HouseNumber: "10", PostalCode: "1234AB"));

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", request, cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(BsnSearchPath.Address, body?.SearchPath);
        Assert.Equal("078211529", body?.Answer?.Person?.Bsn);
    }

    [Fact]
    public async Task VerifyAcceptsBsnAndSurnameSearchPath()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = new BsnVerifyRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(Authorized: true, EmergencyAccess: false),
            Purpose: "patient-verification",
            Bsn: "078211529",
            Person: new BsnPersonInput(
                Surname: "Test-GG-Gevonden",
                BirthDate: "19700101",
                Sex: BsnSex.Male));

        var response = await client.PostAsJsonAsync("/v1/bsn/verify", request, cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Equal(SbvzResult.Good, body?.Result);
        Assert.Equal("2003", Assert.Single(body?.Messages ?? []).Code);
        Assert.Equal("078211529", body?.Answer?.Person?.Bsn);
    }

    [Fact]
    public async Task VerifyRejectsExplicitNullBsnInsteadOfPerformingLookup()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        const string json = """
            {
              "actor": { "id": "fictional-user", "role": "employee" },
              "access": { "authorized": true, "emergencyAccess": false },
              "purpose": "patient-verification",
              "bsn": null,
              "person": {
                "surname": "Test-GG-Gevonden",
                "birthDate": "19700101",
                "sex": "M"
              }
            }
            """;

        var response = await client.PostAsync("/v1/bsn/verify", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task LookupRejectsNumericSexEnumValue()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        const string json = """
            {
              "actor": { "id": "fictional-user", "role": "employee" },
              "access": { "authorized": true, "emergencyAccess": false },
              "purpose": "patient-registration",
              "person": {
                "surname": "Test-GG-Gevonden",
                "birthDate": "19700101",
                "sex": 0
              }
            }
            """;

        var response = await client.PostAsync("/v1/bsn/lookup", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task FunctionalSbvzFailureRemainsAReadableSuccessfulHttpResponse()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = new BsnLookupRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(Authorized: true, EmergencyAccess: false),
            Purpose: "patient-registration",
            Person: new BsnPersonInput(
                Surname: "Unknown",
                BirthDate: "19700101",
                Sex: BsnSex.Male));

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", request, cancellationToken: TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(cancellationToken: TestContext.Current.CancellationToken);
        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        response.EnsureSuccessStatusCode();
        Assert.Equal(SbvzResult.Error, body?.Result);
        Assert.Null(body?.Answer);
        Assert.Equal(JsonValueKind.Null, responseJson.RootElement.GetProperty("answer").ValueKind);
        Assert.Equal("23001", Assert.Single(body?.Messages ?? []).Code);
        Assert.Equal(AuditOutcome.Failed, application.AuditWriter.Entries[^1].Operation.Outcome);
    }

    [Fact]
    public async Task LookupRejectsMissingRequiredPersonBeforeAuditing()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        const string json = """
            {
              "actor": { "id": "fictional-user", "role": "employee" },
              "access": { "authorized": true, "emergencyAccess": false },
              "purpose": "patient-registration"
            }
            """;

        var response = await client.PostAsync("/v1/bsn/lookup", new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("System.Text.Json", responseBody, StringComparison.Ordinal);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task ValidationProblemUsesJsonFieldPaths()
    {
        using var application = new BsnApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = new BsnLookupRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(Authorized: true, EmergencyAccess: false),
            Purpose: "patient-registration",
            Person: new BsnPersonInput(BirthDate: "19700101", Sex: BsnSex.Male),
            Address: new BsnAddressInput(HouseNumber: "10", PostalCode: "1234 AB"));

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", request, cancellationToken: TestContext.Current.CancellationToken);
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(
            problem.RootElement
                .GetProperty("errors")
                .TryGetProperty("address.postalCode", out _));
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.BadGateway)]
    [InlineData(true, HttpStatusCode.GatewayTimeout)]
    public async Task TechnicalFailureReturnsOperationIdThatMatchesAudit(
        bool timeout,
        HttpStatusCode expectedStatus)
    {
        Exception upstreamException = timeout
            ? new TaskCanceledException("Fictional timeout")
            : new HttpRequestException("Fictional upstream failure");
        using var application = new BsnApplicationFactory(new ThrowingSbvzClient(upstreamException));
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        var request = CreateSurnameLookupRequest();

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", request, cancellationToken: TestContext.Current.CancellationToken);
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);
        var operationId = problem.RootElement.GetProperty("operationId").GetGuid();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.NotEqual(Guid.Empty, operationId);
        Assert.Equal(2, application.AuditWriter.Entries.Count);
        Assert.All(
            application.AuditWriter.Entries,
            entry => Assert.Equal(operationId.ToString("D"), entry.OperationId));
        var (failure, alertOperationId) = Assert.Single(application.Alerts.SbvzFailures);
        Assert.Equal(
            timeout ? SbvzTechnicalFailure.Timeout : SbvzTechnicalFailure.TransportOrProtocol,
            failure);
        Assert.Equal(operationId, alertOperationId);
    }

    [Fact]
    public async Task AuditFailureReturnsTraceableServiceUnavailableResponse()
    {
        using var application = new BsnApplicationFactory(failAuditWrites: true);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

        var response = await client.PostAsJsonAsync("/v1/bsn/lookup", CreateSurnameLookupRequest(), cancellationToken: TestContext.Current.CancellationToken);
        using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var operationId = problem.RootElement.GetProperty("operationId").GetGuid();
        Assert.NotEqual(Guid.Empty, operationId);
        Assert.Empty(application.AuditWriter.Entries);
        var (operation, alertOperationId) = Assert.Single(application.Alerts.AuditStorageFailures);
        Assert.Equal(AuditStorageOperation.Write, operation);
        Assert.Equal(operationId, alertOperationId);
    }

    private static BsnLookupRequest CreateSurnameLookupRequest()
    {
        return new BsnLookupRequest(
            Actor: new ApiActor("fictional-user", "employee"),
            Access: new ApiAccessContext(Authorized: true, EmergencyAccess: false),
            Purpose: "patient-registration",
            Person: new BsnPersonInput(
                Surname: "Test-GG-Gevonden",
                BirthDate: "19700101",
                Sex: BsnSex.Male));
    }

    private sealed class BsnApplicationFactory(
        ISbvzClient? sbvzClient = null,
        bool failAuditWrites = false)
        : WebApplicationFactory<Program>
    {
        public RecordingAuditWriter AuditWriter { get; } = new() { FailWrites = failAuditWrites };
        public RecordingSecurityAlertService Alerts { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
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
                        ["SBVZ_ALERT_WEBHOOK_URL"] = string.Empty,
                        ["SBVZ_ALERT_WEBHOOK_URL_FILE"] = string.Empty
                    });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter>(AuditWriter);
                services.RemoveAll<ISecurityAlertService>();
                services.AddSingleton<ISecurityAlertService>(Alerts);
                services.RemoveAll<IEmergencyStop>();
                services.AddSingleton<IEmergencyStop>(new RecordingEmergencyStop());

                if (sbvzClient is not null)
                {
                    services.RemoveAll<ISbvzClient>();
                    services.AddSingleton(sbvzClient);
                }
            });
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];
        public bool FailWrites { get; init; }

        public Task<AuditWriteReceipt> WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites)
            {
                throw new InvalidOperationException("Fictional audit failure");
            }

            Entries.Add(entry);

            return Task.FromResult(new AuditWriteReceipt("fictional-key", "fictional-hash"));
        }
    }

    private sealed class ThrowingSbvzClient(Exception exception) : ISbvzClient
    {
        public Task<SbvzQueryResponse> QueryAsync(
            SbvzPersonQuery query,
            string localReference,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<SbvzQueryResponse>(exception);
        }
    }
}
