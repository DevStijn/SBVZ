using System.Globalization;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Audit;

internal interface IAuditObjectStore
{
    Task WriteOnceAsync(
        string objectKey,
        ReadOnlyMemory<byte> content,
        string contentIntegrity,
        CancellationToken cancellationToken);
}

internal sealed class S3AuditObjectStore(
    IAmazonS3 client,
    IOptions<S3AuditOptions> options) : IAuditObjectStore
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    public async Task WriteOnceAsync(
        string objectKey,
        ReadOnlyMemory<byte> content,
        string contentIntegrity,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        var request = new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = objectKey,
            InputStream = stream,
            ContentType = "application/json",
            IfNoneMatch = "*",
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        request.Metadata["content-integrity"] = contentIntegrity;
        request.Metadata["schema-version"] = AuditEntry.CurrentSchemaVersion.ToString(
            CultureInfo.InvariantCulture);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        await client.PutObjectAsync(request, timeout.Token);
    }
}
