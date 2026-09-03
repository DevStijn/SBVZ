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
        string contentSha256,
        CancellationToken cancellationToken);
}

internal sealed class S3AuditObjectStore(
    IAmazonS3 client,
    IOptions<S3AuditOptions> options) : IAuditObjectStore
{
    public async Task WriteOnceAsync(
        string objectKey,
        ReadOnlyMemory<byte> content,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        var request = new PutObjectRequest
        {
            BucketName = options.Value.Bucket,
            Key = objectKey,
            InputStream = stream,
            ContentType = "application/json",
            IfNoneMatch = "*"
        };
        request.Metadata["content-sha256"] = contentSha256;
        request.Metadata["schema-version"] = AuditEntry.CurrentSchemaVersion.ToString(
            CultureInfo.InvariantCulture);

        await client.PutObjectAsync(request, cancellationToken);
    }
}
