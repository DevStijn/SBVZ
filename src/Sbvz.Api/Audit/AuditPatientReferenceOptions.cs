namespace Sbvz.Api.Audit;

internal sealed class AuditPatientReferenceOptions
{
    public const string KeyIdVariable = "SBVZ_AUDIT_PATIENT_REFERENCE_KEY_ID";
    public const string KeyVariable = "SBVZ_AUDIT_PATIENT_REFERENCE_KEY";
    public const string KeyFileVariable = "SBVZ_AUDIT_PATIENT_REFERENCE_KEY_FILE";

    public string KeyId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}
