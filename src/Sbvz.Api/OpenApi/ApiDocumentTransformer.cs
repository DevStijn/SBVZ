using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Sbvz.Api.OpenApi;

internal sealed class ApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "SBV-Z API",
            Version = "v1",
            Description = "Internal API for retrieving and verifying Dutch citizen service numbers through SBV-Z."
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                Description = "Internal service API key."
            }
        };

        foreach (var path in document.Paths.Where(path =>
                     path.Key.StartsWith("/v1/", StringComparison.Ordinal)))
        {
            if (path.Value is null)
            {
                continue;
            }

            if (path.Value.Operations is null)
            {
                continue;
            }

            foreach (var operation in path.Value.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            }
        }

        return Task.CompletedTask;
    }
}
