namespace Sbvz.Api.Sbvz;

internal sealed class SbvzOptions
{
    public const string ModeVariable = "SBVZ_MODE";
    public const string SubscriberNumberVariable = "SBVZ_SUBSCRIBER_NUMBER";
    public const string CertificatePathVariable = "SBVZ_CLIENT_CERTIFICATE_PATH";
    public const string CertificatePasswordVariable = "SBVZ_CLIENT_CERTIFICATE_PASSWORD";
    public const string CertificatePasswordFileVariable = "SBVZ_CLIENT_CERTIFICATE_PASSWORD_FILE";
    public const string TimeoutSecondsVariable = "SBVZ_TIMEOUT_SECONDS";

    public string Mode { get; set; } = string.Empty;
    public string SubscriberNumber { get; set; } = string.Empty;
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

internal enum SbvzMode
{
    Mock,
    Acceptance,
    Production
}
