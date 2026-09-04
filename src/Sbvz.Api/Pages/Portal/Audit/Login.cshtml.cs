using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Sbvz.Api.Portal;

namespace Sbvz.Api.Pages.Portal.Audit;

[AllowAnonymous]
[EnableRateLimiting(AuditPortalConstants.LoginRateLimitPolicy)]
public sealed class LoginModel(AuditPortalService portalService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Vul de gebruikersnaam in.")]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Vul het wachtwoord in.")]
    [StringLength(1_024)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Vul de verificatiecode in.")]
    [RegularExpression("^[0-9]{6}$", ErrorMessage = "Vul een code van zes cijfers in.")]
    public string TotpCode { get; set; } = string.Empty;

    public bool HasError { get; private set; }
    public bool IsUnavailable { get; private set; }

    public IActionResult OnGet()
    {
        return User.Identity?.IsAuthenticated is true
            ? RedirectToPage("/Portal/Audit/Index")
            : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ClearSecrets();

            return Page();
        }

        bool isValid;

        try
        {
            isValid = await portalService.AuthenticateAsync(Username, Password, TotpCode);
        }
        catch (AuditPortalUnavailableException)
        {
            IsUnavailable = true;
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ClearSecrets();

            return Page();
        }

        if (!isValid)
        {
            HasError = true;
            Username = string.Empty;
            ModelState.Remove(nameof(Username));
            ClearSecrets();

            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Username),
            new Claim(ClaimTypes.Name, Username),
            new Claim(ClaimTypes.Role, AuditPortalConstants.AdministratorRole)
        };
        var identity = new ClaimsIdentity(
            claims,
            AuditPortalConstants.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            AuditPortalConstants.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                AllowRefresh = false,
                IsPersistent = false
            });
        ClearSecrets();

        return RedirectToPage("/Portal/Audit/Index");
    }

    private void ClearSecrets()
    {
        Password = string.Empty;
        TotpCode = string.Empty;
        ModelState.Remove(nameof(Password));
        ModelState.Remove(nameof(TotpCode));
    }
}
