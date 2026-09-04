namespace Sbvz.Api.Portal;

internal sealed class AuditPortalOptions
{
    public const string EnabledVariable = "SBVZ_AUDIT_PORTAL_ENABLED";
    public const string UsernameVariable = "SBVZ_AUDIT_PORTAL_USERNAME";
    public const string PasswordHashVariable = "SBVZ_AUDIT_PORTAL_PASSWORD_HASH";
    public const string PasswordHashFileVariable = "SBVZ_AUDIT_PORTAL_PASSWORD_HASH_FILE";
    public const string TotpSecretVariable = "SBVZ_AUDIT_PORTAL_TOTP_SECRET";
    public const string TotpSecretFileVariable = "SBVZ_AUDIT_PORTAL_TOTP_SECRET_FILE";

    public bool Enabled { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string TotpSecret { get; set; } = string.Empty;
}
