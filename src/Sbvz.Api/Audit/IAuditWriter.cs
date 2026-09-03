namespace Sbvz.Api.Audit;

public interface IAuditWriter
{
    Task<AuditWriteReceipt> WriteAsync(
        AuditEntry entry,
        CancellationToken cancellationToken = default);
}

public sealed record AuditWriteReceipt(string ObjectKey, string ContentSha256);
