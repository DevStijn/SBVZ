using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OtpNet;
using Sbvz.Api.Alerting;
using Sbvz.Api.Audit;
using Sbvz.Api.Portal;
using Sbvz.Api.Safety;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed partial class AuditPortalEndpointTests
{
    private const string Username = "admin";
    private const string Password = "fictional-password-for-tests";

    [Fact]
    public async Task RedirectsAnonymousUserToFixedLoginPage()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var response = await client.GetAsync("/portal/audit", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/portal/audit/login", response.Headers.Location?.OriginalString);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task AuthenticatedUserCanReadAuditedPageWithoutRawBsn()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        await LoginAsync(client, application.TotpSecretBytes);

        var response = await client.GetAsync("/portal/audit?date=2026-09-03", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("fictional-record", html, StringComparison.Ordinal);
        Assert.Contains("fictional-user", html, StringComparison.Ordinal);
        Assert.DoesNotContain("123456782", html, StringComparison.Ordinal);
        Assert.Collection(
            application.AuditWriter.Entries,
            entry => Assert.Equal("portal-login", entry.Operation.Name),
            entry => Assert.Equal("portal-login", entry.Operation.Name),
            entry => Assert.Equal("view-audit", entry.Operation.Name),
            entry => Assert.Equal("view-audit", entry.Operation.Name));
        Assert.Equal(
            AuditOutcome.Succeeded,
            application.AuditWriter.Entries[^1].Operation.Outcome);
        Assert.Equal(
            AuthenticationSurface.AuditPortal,
            Assert.Single(application.Alerts.AuthenticationSuccesses));
    }

    [Fact]
    public async Task AuthenticatedAdministratorCanActivateEmergencyStop()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        await LoginAsync(client, application.TotpSecretBytes);
        var token = await GetAntiforgeryTokenAsync(
            client,
            "/portal/audit/emergency-stop");
        using var body = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            });

        var response = await client.PostAsync(
            "/portal/audit/emergency-stop?handler=Activate",
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/portal/audit/emergency-stop",
            response.Headers.Location?.OriginalString);
        Assert.Equal(EmergencyStopStatus.Active, application.EmergencyStop.Status);
        var actor = Assert.Single(application.EmergencyStop.Activations);
        Assert.Equal(Username, actor.Id);
        Assert.Equal("portal-administrator", actor.Role);

        var result = await client.GetAsync(
            response.Headers.Location,
            TestContext.Current.CancellationToken);
        var html = await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        result.EnsureSuccessStatusCode();
        Assert.Contains("De noodstop is geactiveerd.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("_control/sbvz-disabled", html, StringComparison.Ordinal);
        Assert.DoesNotContain("objectopslag", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Noodstop uitschakelen", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmergencyStopActivationRequiresAntiforgeryToken()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        await LoginAsync(client, application.TotpSecretBytes);
        using var body = new FormUrlEncodedContent([]);

        var response = await client.PostAsync(
            "/portal/audit/emergency-stop?handler=Activate",
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(application.EmergencyStop.Activations);
    }

    [Fact]
    public async Task AnonymousUserCannotOpenEmergencyStopPage()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var response = await client.GetAsync(
            "/portal/audit/emergency-stop",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/portal/audit/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task InvalidLoginIsRejectedAndAuditedWithoutSubmittedUsername()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        var token = await GetAntiforgeryTokenAsync(client);
        using var body = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Username"] = "submitted-user",
                ["Password"] = "wrong-password",
                ["TotpCode"] = "123456"
            });

        var response = await client.PostAsync("/portal/audit/login", body, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var protectedResponse = await client.GetAsync("/portal/audit", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "Gebruikersnaam, wachtwoord of verificatiecode is onjuist.",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("submitted-user", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        Assert.Equal(2, application.AuditWriter.Entries.Count);
        Assert.All(
            application.AuditWriter.Entries,
            entry => Assert.Equal("anonymous", entry.Actor.Id));
        Assert.Equal(
            AuditOutcome.Failed,
            application.AuditWriter.Entries[^1].Operation.Outcome);
        Assert.Equal(
            AuthenticationSurface.AuditPortal,
            Assert.Single(application.Alerts.AuthenticationFailures));
    }

    [Fact]
    public async Task LoginPostRequiresAntiforgeryToken()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        using var body = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Username"] = Username,
                ["Password"] = Password,
                ["TotpCode"] = "123456"
            });

        var response = await client.PostAsync("/portal/audit/login", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(application.AuditWriter.Entries);
    }

    [Fact]
    public async Task StaleAnonymousLoginFormRedirectsWhenSessionIsAlreadyAuthenticated()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        var staleAnonymousToken = await GetAntiforgeryTokenAsync(client);
        await LoginAsync(client, application.TotpSecretBytes);
        using var staleBody = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = staleAnonymousToken,
                ["Username"] = Username,
                ["Password"] = Password,
                ["TotpCode"] = new Totp(application.TotpSecretBytes).ComputeTotp()
            });

        var response = await client.PostAsync("/portal/audit/login", staleBody, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/portal/audit", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginRateLimitQueuesSecurityAlert()
    {
        using var application = new AuditPortalApplicationFactory();
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var token = await GetAntiforgeryTokenAsync(client);

        for (var request = 0; request < 10; request++)
        {
            using var body = InvalidLoginBody(token);
            var response = await client.PostAsync("/portal/audit/login", body, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rejectedBody = InvalidLoginBody(token);
        var rejected = await client.PostAsync("/portal/audit/login", rejectedBody, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(
            AuthenticationSurface.AuditPortal,
            Assert.Single(application.Alerts.RateLimits));
    }

    [Fact]
    public async Task PortalRoutesDoNotExistWhenDisabled()
    {
        using var application = new HealthApplicationFactory();
        using var client = application.CreateClient();

        var response = await client.GetAsync("/portal/audit/login", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task LoginAsync(HttpClient client, byte[] totpSecretBytes)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        var totpCode = new Totp(totpSecretBytes).ComputeTotp();
        using var body = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Username"] = Username,
                ["Password"] = Password,
                ["TotpCode"] = totpCode
            });

        var response = await client.PostAsync("/portal/audit/login", body);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/portal/audit", response.Headers.Location?.OriginalString);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path = "/portal/audit/login")
    {
        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenPattern().Match(html);

        response.EnsureSuccessStatusCode();
        Assert.True(match.Success);

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent InvalidLoginBody(string token)
    {
        return new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Username"] = "invalid-user",
                ["Password"] = "invalid-password",
                ["TotpCode"] = "123456"
            });
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();
}

internal sealed class AuditPortalApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataProtectionPath = Path.Combine(
        Path.GetTempPath(),
        $"sbvz-audit-portal-{Guid.NewGuid():N}");

    public AuditPortalApplicationFactory()
    {
        Directory.CreateDirectory(_dataProtectionPath);
        TotpSecretBytes = [.. Enumerable.Range(1, 32).Select(value => (byte)value)];
    }

    public byte[] TotpSecretBytes { get; }
    public RecordingPortalAuditWriter AuditWriter { get; } = new();
    public RecordingSecurityAlertService Alerts { get; } = new();
    public RecordingEmergencyStop EmergencyStop { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var hasher = new PasswordHasher<AuditPortalUser>(
            Options.Create(
                new PasswordHasherOptions
                {
                    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                    IterationCount = 600_000
                }));
        var passwordHash = hasher.HashPassword(new AuditPortalUser("admin"), "fictional-password-for-tests");

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SBVZ_MODE"] = "Mock",
                    ["SBVZ_SUBSCRIBER_NUMBER"] = "12345678",
                    ["SBVZ_API_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    ["SBVZ_AUDIT_S3_BUCKET"] = "fictional-bucket",
                    ["SBVZ_AUDIT_S3_ENDPOINT"] = "https://storage.example",
                    ["SBVZ_AUDIT_S3_REGION"] = "fictional-region",
                    ["SBVZ_AUDIT_S3_PREFIX"] = "audit",
                    ["SBVZ_AUDIT_S3_ACCESS_KEY_ID"] = "fictional-access-key",
                    ["SBVZ_AUDIT_S3_SECRET_ACCESS_KEY"] = "fictional-secret-key",
                    ["SBVZ_AUDIT_PATIENT_REFERENCE_KEY_ID"] = "test-v1",
                    ["SBVZ_AUDIT_PATIENT_REFERENCE_KEY"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    ["SBVZ_AUDIT_PORTAL_ENABLED"] = "true",
                    ["SBVZ_AUDIT_PORTAL_USERNAME"] = "admin",
                    ["SBVZ_AUDIT_PORTAL_PASSWORD_HASH"] = passwordHash,
                    ["SBVZ_AUDIT_PORTAL_TOTP_SECRET"] = Base32Encoding.ToString(TotpSecretBytes),
                    ["SBVZ_AUDIT_PORTAL_KEYS_PATH"] = _dataProtectionPath,
                    ["SBVZ_ALERT_WEBHOOK_URL"] = string.Empty,
                    ["SBVZ_ALERT_WEBHOOK_URL_FILE"] = string.Empty
                });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditReader>();
            services.RemoveAll<IAuditWriter>();
            services.RemoveAll<ISecurityAlertService>();
            services.RemoveAll<IEmergencyStop>();
            services.AddSingleton<IAuditReader>(new FictionalAuditReader());
            services.AddSingleton<IAuditWriter>(AuditWriter);
            services.AddSingleton<ISecurityAlertService>(Alerts);
            services.AddSingleton<IEmergencyStop>(EmergencyStop);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_dataProtectionPath))
        {
            Directory.Delete(_dataProtectionPath, recursive: true);
        }
    }
}

internal sealed class FictionalAuditReader : IAuditReader
{
    public Task<AuditPage> ReadPageAsync(
        DateOnly auditDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var entry = new AuditOperationRecord(
            SchemaVersion: AuditEntry.CurrentSchemaVersion,
            AttemptEventId: Guid.Parse("01990f73-4963-7c51-a54f-83d482033731"),
            CompletionEventId: Guid.Parse("01990f73-4963-7c51-a54f-83d482033732"),
            StartedAtUtc: new DateTimeOffset(2026, 9, 3, 9, 29, 59, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 9, 3, 9, 30, 0, TimeSpan.Zero),
            OperationId: "01990f73-4963-7c51-a54f-83d482033733",
            TraceId: "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            Invalidated: false,
            SubscriberNumber: "12345678",
            PatientReference: $"hmac-sha256:test-v1:{new string('a', 64)}",
            RecordId: "fictional-record",
            ActorId: "fictional-user",
            ActorRole: "employee",
            Authorized: true,
            TreatmentRelationship: true,
            Consent: true,
            EmergencyAccess: false,
            OperationName: "lookup-bsn",
            Purpose: "patient-registration",
            ActionType: AuditActionType.Query,
            DataCategory: AuditDataCategory.PatientIdentification,
            Outcome: AuditOutcome.Succeeded,
            ResponseCode: "success",
            DurationMilliseconds: 125);

        return Task.FromResult(
            new AuditPage([entry], page, pageSize, TotalPages: 1, TotalCount: 1));
    }
}

internal sealed class RecordingPortalAuditWriter : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public Task<AuditWriteReceipt> WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);

        return Task.FromResult(new AuditWriteReceipt("fictional-key", "fictional-hash"));
    }
}
