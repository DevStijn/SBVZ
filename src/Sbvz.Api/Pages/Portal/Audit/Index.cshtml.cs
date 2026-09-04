using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sbvz.Api.Audit;
using Sbvz.Api.Portal;
using Sbvz.Api.Safety;

namespace Sbvz.Api.Pages.Portal.Audit;

[Authorize(Policy = AuditPortalConstants.AuthorizationPolicy)]
public sealed class IndexModel(
    AuditPortalService portalService,
    TimeProvider timeProvider,
    IEmergencyStop emergencyStop) : PageModel
{
    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    [BindProperty(SupportsGet = true)]
    [Range(1, 100_000)]
    public int PageNumber { get; set; } = 1;

    public AuditPage? AuditPage { get; private set; }
    public bool IsUnavailable { get; private set; }
    public bool IsEmergencyStopActive { get; private set; }
    public bool IsEmergencyStopUnavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Date == default)
        {
            Date = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        }

        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;

            return Page();
        }

        var username = User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(username))
        {
            return Forbid();
        }

        try
        {
            var stopStatus = await emergencyStop.GetStatusAsync(cancellationToken);
            IsEmergencyStopActive = stopStatus is EmergencyStopStatus.Active;
            IsEmergencyStopUnavailable = stopStatus is EmergencyStopStatus.Unavailable;
            AuditPage = await portalService.ReadAsync(
                username,
                Date,
                PageNumber,
                PageSize,
                cancellationToken);
        }
        catch (AuditPortalUnavailableException)
        {
            IsUnavailable = true;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(AuditPortalConstants.AuthenticationScheme);

        return RedirectToPage("/Portal/Audit/Login");
    }

    public static string OutcomeLabel(AuditOutcome outcome)
    {
        return outcome switch
        {
            AuditOutcome.Attempted => "Onvoltooid",
            AuditOutcome.Succeeded => "Geslaagd",
            AuditOutcome.Failed => "Mislukt",
            AuditOutcome.Cancelled => "Geannuleerd",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
        };
    }

    public static string DurationLabel(int? durationMilliseconds)
    {
        return durationMilliseconds is null ? "—" : $"{durationMilliseconds} ms";
    }

    public static string TimestampLabel(DateTimeOffset value)
    {
        return value.ToString(
            "yyyy-MM-dd HH:mm:ss.fff 'UTC'",
            CultureInfo.InvariantCulture);
    }

    public static string OptionalTimestampLabel(DateTimeOffset? value)
    {
        return value is null ? "—" : TimestampLabel(value.Value);
    }

    public static string BooleanLabel(bool? value)
    {
        return value switch
        {
            true => "Ja",
            false => "Nee",
            null => "Niet van toepassing"
        };
    }

    public static string OperationLabel(string operationName)
    {
        return operationName switch
        {
            "lookup-bsn" => "BSN opgevraagd",
            "verify-bsn" => "BSN geverifieerd",
            "portal-login" => "Inlogpoging auditportaal",
            "view-audit" => "Auditlog bekeken",
            "emergency-stop" => "Noodstopstatus gewijzigd",
            _ => operationName
        };
    }

    public static string PurposeLabel(string purpose)
    {
        return purpose switch
        {
            "portal-authentication" => "Authenticatie",
            "audit-review" => "Toegangscontrole",
            "service-protection" => "Beveiliging van de service",
            _ => purpose
        };
    }

    public static string ActionTypeLabel(AuditActionType actionType)
    {
        return actionType switch
        {
            AuditActionType.Read => "Inzage",
            AuditActionType.Query => "Zoekactie",
            AuditActionType.Security => "Beveiligingsactie",
            _ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, null)
        };
    }

    public static string DataCategoryLabel(AuditDataCategory dataCategory)
    {
        return dataCategory switch
        {
            AuditDataCategory.PatientIdentification => "Patiëntidentificatie",
            AuditDataCategory.AuditLog => "Auditlog",
            AuditDataCategory.Service => "Service",
            _ => throw new ArgumentOutOfRangeException(nameof(dataCategory), dataCategory, null)
        };
    }

    public static string AbbreviateOperationId(string operationId)
    {
        return operationId.Length <= 12 ? operationId : $"{operationId[..12]}…";
    }

    public static string AbbreviatePatientReference(string patientReference)
    {
        var finalSeparator = patientReference.LastIndexOf(':');
        var value = finalSeparator >= 0 ? patientReference[(finalSeparator + 1)..] : patientReference;

        return value.Length <= 12 ? value : $"{value[..12]}…";
    }
}
