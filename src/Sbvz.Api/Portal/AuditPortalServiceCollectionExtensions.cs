using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OtpNet;
using Sbvz.Api.Alerting;
using Sbvz.Api.Audit;
using Sbvz.Api.Configuration;

namespace Sbvz.Api.Portal;

public static class AuditPortalServiceCollectionExtensions
{
    private const int PasswordHashIterations = 600_000;

    public static IServiceCollection AddAuditPortal(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services
            .AddOptions<AuditPortalOptions>()
            .Configure(options =>
            {
                options.Enabled = ParseEnabled(configuration[AuditPortalOptions.EnabledVariable]);
                options.Username = configuration[AuditPortalOptions.UsernameVariable] ?? string.Empty;
                options.PasswordHash = SecretValueResolver.Resolve(
                    configuration[AuditPortalOptions.PasswordHashVariable],
                    configuration[AuditPortalOptions.PasswordHashFileVariable]);
                options.TotpSecret = SecretValueResolver.Resolve(
                    configuration[AuditPortalOptions.TotpSecretVariable],
                    configuration[AuditPortalOptions.TotpSecretFileVariable]);
                options.DataProtectionKeysPath =
                    configuration[AuditPortalOptions.DataProtectionKeysPathVariable] ?? string.Empty;
            })
            .Validate(
                options => !options.Enabled || IsValidUsername(options.Username),
                $"{AuditPortalOptions.UsernameVariable} must be set to a valid username when the audit portal is enabled.")
            .Validate(
                options => !options.Enabled || IsValidPasswordHash(options.PasswordHash),
                $"{AuditPortalOptions.PasswordHashVariable} or {AuditPortalOptions.PasswordHashFileVariable} must contain an ASP.NET Core Identity password hash when the audit portal is enabled.")
            .Validate(
                options => !options.Enabled || IsStrongTotpSecret(options.TotpSecret),
                $"{AuditPortalOptions.TotpSecretVariable} or {AuditPortalOptions.TotpSecretFileVariable} must contain a Base32-encoded key of at least 20 bytes when the audit portal is enabled.")
            .Validate(
                options => !options.Enabled || IsValidKeysPath(options.DataProtectionKeysPath),
                $"{AuditPortalOptions.DataProtectionKeysPathVariable} must point to an existing absolute directory when the audit portal is enabled.")
            .ValidateOnStart();

        services
            .AddDataProtection()
            .SetApplicationName("SBVZ.AuditPortal");
        services
            .AddOptions<KeyManagementOptions>()
            .Configure<IOptions<AuditPortalOptions>, ILoggerFactory>(
                (keyOptions, portalOptions, loggerFactory) =>
                {
                    if (portalOptions.Value.Enabled)
                    {
                        keyOptions.XmlRepository = new FileSystemXmlRepository(
                            new DirectoryInfo(portalOptions.Value.DataProtectionKeysPath),
                            loggerFactory);
                    }
                });

        services
            .AddAuthentication(AuditPortalConstants.AuthenticationScheme)
            .AddCookie(AuditPortalConstants.AuthenticationScheme, options =>
            {
                options.Cookie.Name = environment.IsDevelopment()
                    ? "Sbvz.AuditPortal.Admin"
                    : "__Host-SbvzAuditPortalAdmin";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.LoginPath = "/portal/audit/login";
                options.SlidingExpiration = false;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.Redirect("/portal/audit/login");
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                AuditPortalConstants.AuthorizationPolicy,
                policy => policy
                    .AddAuthenticationSchemes(AuditPortalConstants.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireRole(AuditPortalConstants.AdministratorRole));
        services
            .AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder(
                    "/Portal/Audit",
                    AuditPortalConstants.AuthorizationPolicy);
                options.Conventions.AllowAnonymousToPage("/Portal/Audit/Login");
            });
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = environment.IsDevelopment()
                ? "Sbvz.AuditPortal.Antiforgery"
                : "__Host-SbvzAuditPortalAntiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                var surface = context.HttpContext.Request.Path.StartsWithSegments("/v1")
                    ? AuthenticationSurface.InternalApi
                    : AuthenticationSurface.AuditPortal;
                context.HttpContext.RequestServices
                    .GetRequiredService<ISecurityAlertService>()
                    .RateLimitExceeded(surface);

                return ValueTask.CompletedTask;
            };
            options.AddPolicy(
                AuditPortalConstants.LoginRateLimitPolicy,
                context => HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 10,
                            QueueLimit = 0,
                            Window = TimeSpan.FromMinutes(5)
                        })
                    : RateLimitPartition.GetNoLimiter("portal-login-read"));
        });
        services.AddSingleton<IPasswordHasher<AuditPortalUser>>(
            _ => new PasswordHasher<AuditPortalUser>(
                Options.Create(
                    new PasswordHasherOptions
                    {
                        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                        IterationCount = PasswordHashIterations
                    })));
        services.AddSingleton<IAuditPortalCredentialValidator, AuditPortalCredentialValidator>();
        services.AddSingleton(provider => new AuditPortalService(
            provider.GetRequiredService<IAuditReader>(),
            provider.GetRequiredService<IAuditWriter>(),
            provider.GetRequiredService<IAuditPortalCredentialValidator>(),
            provider.GetRequiredService<IOptions<global::Sbvz.Api.Sbvz.SbvzOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ISecurityAlertService>(),
            provider.GetRequiredService<ILogger<AuditPortalService>>()));

        return services;
    }

    private static bool ParseEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (bool.TryParse(value, out var enabled))
        {
            return enabled;
        }

        throw new InvalidOperationException(
            $"{AuditPortalOptions.EnabledVariable} must be true or false.");
    }

    private static bool IsValidUsername(string value)
    {
        return value.Length is >= 1 and <= 100
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && !value.Any(char.IsControl);
    }

    private static bool IsValidPasswordHash(string value)
    {
        byte[]? decoded = null;

        try
        {
            if (value.Length is < 80 or > 1_024
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            decoded = Convert.FromBase64String(value);

            if (decoded.Length < 61 || decoded[0] != 0x01)
            {
                return false;
            }

            var prf = BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(1, 4));
            var iterations = BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(5, 4));
            var saltLength = BinaryPrimitives.ReadUInt32BigEndian(decoded.AsSpan(9, 4));

            return prf == 2
                && iterations >= PasswordHashIterations
                && iterations <= 10_000_000
                && saltLength is >= 16 and <= 64
                && decoded.Length == 13 + saltLength + 32;
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
    }

    private static bool IsStrongTotpSecret(string value)
    {
        byte[]? decoded = null;

        try
        {
            if (value.Length is < 32 or > 1_024
                || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            decoded = Base32Encoding.ToBytes(value);

            return decoded.Length >= 20;
        }
        catch (ArgumentException)
        {
            return false;
        }
        finally
        {
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
    }

    private static bool IsValidKeysPath(string value)
    {
        return Path.IsPathFullyQualified(value) && Directory.Exists(value);
    }
}
