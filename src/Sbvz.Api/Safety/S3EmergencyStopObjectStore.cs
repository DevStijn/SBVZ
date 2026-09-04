using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Sbvz.Api.Audit;

namespace Sbvz.Api.Safety;

internal interface IEmergencyStopObjectStore
{
    Task<bool> ExistsAsync(CancellationToken cancellationToken);

    Task CreateIfMissingAsync(CancellationToken cancellationToken);
}

internal sealed class S3EmergencyStopObjectStore(
    IAmazonS3 client,
    IOptions<S3AuditOptions> auditOptions,
    IOptions<EmergencyStopOptions> emergencyStopOptions) : IEmergencyStopObjectStore
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);

        try
        {
            _ = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = auditOptions.Value.Bucket,
                    Key = emergencyStopOptions.Value.ObjectKey
                },
                timeout.Token);

            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task CreateIfMissingAsync(CancellationToken cancellationToken)
    {
        using var content = new MemoryStream([], writable: false);
        using var timeout = CreateTimeout(cancellationToken);
        var request = new PutObjectRequest
        {
            BucketName = auditOptions.Value.Bucket,
            Key = emergencyStopOptions.Value.ObjectKey,
            InputStream = content,
            ContentType = "application/octet-stream",
            IfNoneMatch = "*",
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };

        try
        {
            _ = await client.PutObjectAsync(request, timeout.Token);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed)
        {
            // The marker already exists, so the emergency stop is already active.
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);

        return timeout;
    }
}
