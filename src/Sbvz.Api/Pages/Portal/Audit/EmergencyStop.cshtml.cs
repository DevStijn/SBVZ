using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sbvz.Api.Audit;
using Sbvz.Api.Portal;
using Sbvz.Api.Safety;

namespace Sbvz.Api.Pages.Portal.Audit;

[Authorize(Policy = AuditPortalConstants.AuthorizationPolicy)]
public sealed class EmergencyStopModel(IEmergencyStop emergencyStop) : PageModel
{
    [TempData]
    public bool ActivationSucceeded { get; set; }

    public EmergencyStopStatus Status { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Status = await emergencyStop.GetStatusAsync(cancellationToken);

        if (Status is EmergencyStopStatus.Unavailable)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostActivateAsync(CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(username))
        {
            return Forbid();
        }

        try
        {
            await emergencyStop.ActivateAsync(
                new AuditActor(username, AuditPortalConstants.AdministratorRole),
                cancellationToken);
        }
        catch (EmergencyStopActivationException)
        {
            Status = EmergencyStopStatus.Unavailable;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return Page();
        }

        ActivationSucceeded = true;

        return RedirectToPage();
    }
}
