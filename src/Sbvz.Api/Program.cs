using System.Text.Json.Serialization;
using Sbvz.Api.Api;
using Sbvz.Api.Audit;
using Sbvz.Api.Configuration;
using Sbvz.Api.OpenApi;
using Sbvz.Api.Sbvz;
using Scalar.AspNetCore;

LocalEnvironmentLoader.LoadWhenDevelopment();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddAuditLogging(builder.Configuration);
builder.Services.AddSbvzClient(builder.Configuration);
builder.Services.AddInternalApi(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiDocumentTransformer>();
});

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.UseStatusCodePages();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

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

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .WithName("Health")
    .WithSummary("Service health")
    .Produces<HealthResponse>();
app.MapBsnEndpoints();

app.Run();

public partial class Program;
