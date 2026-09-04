using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sbvz.Api.Alerting;
using Sbvz.Api.Api;
using Sbvz.Api.Audit;
using Sbvz.Api.Configuration;
using Sbvz.Api.Health;
using Sbvz.Api.OpenApi;
using Sbvz.Api.Portal;
using Sbvz.Api.Safety;
using Sbvz.Api.Sbvz;
using Scalar.AspNetCore;

LocalEnvironmentLoader.LoadWhenDevelopment();

var builder = WebApplication.CreateBuilder(args);

SecureHostingConfiguration.Configure(builder);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddAuditLogging(builder.Configuration);
builder.Services.AddSbvzClient(builder.Configuration);
builder.Services.AddInternalApi(builder.Configuration, builder.Environment);
builder.Services.AddSecurityAlerting(builder.Configuration);
builder.Services.AddEmergencyStop(builder.Configuration);
builder.Services.AddAuditPortal(builder.Configuration, builder.Environment);
builder.Services
    .AddHealthChecks()
    .AddCheck<EmergencyStopHealthCheck>("emergency-stop", tags: ["ready"])
    .AddCheck<SbvzCertificateHealthCheck>("uzi-certificate", tags: ["ready"]);
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd(
            "traceId",
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
    };
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiDocumentTransformer>();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseHostFiltering();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();
app.UseStaticFiles();
app.UseMiddleware<AuditPortalSecurityHeadersMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/portal/audit/login")
        && context.User.Identity?.IsAuthenticated is true
        && context.User.IsInRole(AuditPortalConstants.AdministratorRole))
    {
        context.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = "/portal/audit";

        return;
    }

    await next(context);
});
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("SBV-Z API")
            .DisableAgent()
            .DisableDefaultFonts();
    });
}

app.MapHealthChecks(
        "/health",
        new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponseAsync
        })
    .WithName("Health")
    .WithSummary("Service health");
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponseAsync
        })
    .WithName("Readiness")
    .WithSummary("Service readiness");
app.MapBsnEndpoints();

if (app.Services.GetRequiredService<IOptions<AuditPortalOptions>>().Value.Enabled)
{
    app.MapRazorPages();
}

app.Run();

static Task WriteHealthResponseAsync(
    HttpContext context,
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    return context.Response.WriteAsJsonAsync(
        new HealthResponse(
            report.Status is Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
                ? "ok"
                : "unavailable"));
}
