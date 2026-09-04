using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;

namespace Sbvz.Api.Api;

internal sealed partial class ApiKeyAuthenticationMiddleware(
    RequestDelegate next,
    IOptions<ApiAccessOptions> options,
    ISecurityAlertService alerts,
    ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";

        if (!authorization.StartsWith(prefix, StringComparison.Ordinal)
            || !Matches(authorization[prefix.Length..], options.Value.ApiKey))
        {
            LogAuthenticationFailed(logger);
            alerts.AuthenticationFailed(AuthenticationSurface.InternalApi);
            context.Response.Headers.WWWAuthenticate = "Bearer";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool Matches(string provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Internal API authentication failed.")]
    private static partial void LogAuthenticationFailed(ILogger logger);
}
