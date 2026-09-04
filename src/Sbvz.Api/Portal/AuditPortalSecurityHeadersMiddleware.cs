namespace Sbvz.Api.Portal;

internal sealed class AuditPortalSecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isPortal = context.Request.Path.StartsWithSegments("/portal");
        var isApi = context.Request.Path.StartsWithSegments("/v1");

        if (isPortal || isApi)
        {
            context.Response.OnStarting(static state =>
            {
                var response = ((HttpContext)state).Response;
                response.Headers.CacheControl = "no-cache, no-store";
                response.Headers.Pragma = "no-cache";

                return Task.CompletedTask;
            }, context);
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";

            context.Response.Headers.ContentSecurityPolicy = isPortal
                ? "default-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'"
                : "default-src 'none'; frame-ancestors 'none'";
        }

        await next(context);
    }
}
