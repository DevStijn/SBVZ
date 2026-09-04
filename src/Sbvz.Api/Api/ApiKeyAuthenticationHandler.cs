using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;

namespace Sbvz.Api.Api;

internal sealed partial class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<ApiAccessOptions> accessOptions,
    ISecurityAlertService alerts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorizationValues = Request.Headers.Authorization;

        if (authorizationValues.Count != 1
            || !AuthenticationHeaderValue.TryParse(authorizationValues.ToString(), out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(authorization.Parameter)
            || authorization.Parameter.Length > 4_096
            || authorization.Parameter.Any(char.IsWhiteSpace)
            || !Matches(authorization.Parameter, accessOptions.Value.ApiKeySha256))
        {
            LogAuthenticationFailed(Logger);
            alerts.AuthenticationFailed(AuthenticationSurface.InternalApi);

            return Task.FromResult(AuthenticateResult.Fail("Invalid API credential."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, accessOptions.Value.ClientId),
            new Claim(ClaimTypes.Name, accessOptions.Value.ClientId)
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
        };

        await Results.Problem(problem).ExecuteAsync(Context);
    }

    private static bool Matches(string provided, string expectedHash)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        byte[] expected;

        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            CryptographicOperations.ZeroMemory(providedHash);
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(providedHash, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(providedHash);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Internal API authentication failed.")]
    private static partial void LogAuthenticationFailed(ILogger logger);
}
