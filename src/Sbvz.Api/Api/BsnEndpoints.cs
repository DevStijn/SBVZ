using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using Sbvz.Api.Safety;
using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Api;

public static class BsnEndpoints
{
    public static IEndpointRouteBuilder MapBsnEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/v1/bsn")
            .RequireAuthorization(ApiAccessOptions.AuthorizationPolicy)
            .RequireRateLimiting(ApiAccessOptions.RateLimitPolicy)
            .WithTags("BSN");

        group
            .MapPost("/lookup", LookupAsync)
            .WithName("LookupBsn")
            .WithSummary("Retrieve a BSN")
            .WithDescription(
                "Retrieves a BSN using either birth date, sex, postal code and house number "
                + "(search path 1), or birth date, sex and surname (search path 2). "
                + "Supplying a surname always selects search path 2. Other supported person and address fields are optional.")
            .Accepts<BsnLookupRequest>("application/json")
            .Produces<BsnOperationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
        group
            .MapPost("/verify", VerifyAsync)
            .WithName("VerifyBsn")
            .WithSummary("Verify a BSN")
            .WithDescription(
                "Verifies a BSN using the same two identifying-data search paths as lookup. "
                + "Supplying a surname always selects search path 2.")
            .Accepts<BsnVerifyRequest>("application/json")
            .Produces<BsnOperationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return endpoints;
    }

    private static Task<IResult> LookupAsync(
        BsnLookupRequest request,
        BsnOperationService service,
        HttpContext context)
    {
        return ExecuteAsync(() => service.LookupAsync(
            request,
            GetApiClientId(context.User),
            context.RequestAborted));
    }

    private static Task<IResult> VerifyAsync(
        BsnVerifyRequest request,
        BsnOperationService service,
        HttpContext context)
    {
        return ExecuteAsync(() => service.VerifyAsync(
            request,
            GetApiClientId(context.User),
            context.RequestAborted));
    }

    private static string GetApiClientId(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The authenticated API client has no identifier.");
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<BsnOperationResponse>> operation)
    {
        try
        {
            return TypedResults.Ok(await operation());
        }
        catch (SbvzValidationException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [exception.Field] = [exception.Message]
                });
        }
        catch (ArgumentNullException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [exception.ParamName ?? "request"] = ["Required value is missing."]
                });
        }
        catch (AuditUnavailableException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Audit storage unavailable",
                extensions: OperationExtensions(exception.OperationId));
        }
        catch (SbvzAccessDeniedException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Access refused",
                extensions: OperationExtensions(exception.OperationId));
        }
        catch (EmergencyStopException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "BSN operations are temporarily disabled",
                extensions: OperationExtensions(exception.OperationId));
        }
        catch (SbvzOperationException exception) when (exception.Failure is SbvzOperationFailure.Timeout)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "SBV-Z request timed out",
                extensions: OperationExtensions(exception.OperationId));
        }
        catch (SbvzOperationException exception) when (exception.Failure is SbvzOperationFailure.Upstream)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "SBV-Z request failed",
                extensions: OperationExtensions(exception.OperationId));
        }
    }

    private static Dictionary<string, object?> OperationExtensions(Guid operationId)
    {
        return new Dictionary<string, object?>
        {
            ["operationId"] = operationId
        };
    }
}

internal sealed class EmergencyStopException(
    Guid operationId,
    EmergencyStopStatus status) : Exception("BSN operations are temporarily disabled.")
{
    public Guid OperationId { get; } = operationId;
    public EmergencyStopStatus Status { get; } = status;
}
