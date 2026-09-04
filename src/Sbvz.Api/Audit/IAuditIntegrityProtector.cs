namespace Sbvz.Api.Audit;

internal interface IAuditIntegrityProtector
{
    string Protect(string objectKey, ReadOnlySpan<byte> content);

    bool Verify(string objectKey, ReadOnlySpan<byte> content, string? integrityValue);
}
