namespace Sbvz.Api.Audit;

internal sealed class S3AuditOptions
{
    public const string BucketVariable = "SBVZ_AUDIT_S3_BUCKET";
    public const string EndpointVariable = "SBVZ_AUDIT_S3_ENDPOINT";
    public const string RegionVariable = "SBVZ_AUDIT_S3_REGION";
    public const string PrefixVariable = "SBVZ_AUDIT_S3_PREFIX";
    public const string AccessKeyIdVariable = "SBVZ_AUDIT_S3_ACCESS_KEY_ID";
    public const string AccessKeyIdFileVariable = "SBVZ_AUDIT_S3_ACCESS_KEY_ID_FILE";
    public const string SecretAccessKeyVariable = "SBVZ_AUDIT_S3_SECRET_ACCESS_KEY";
    public const string SecretAccessKeyFileVariable = "SBVZ_AUDIT_S3_SECRET_ACCESS_KEY_FILE";

    public string Bucket { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
}
