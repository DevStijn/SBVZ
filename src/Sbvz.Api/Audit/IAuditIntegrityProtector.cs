namespace Sbvz.Api.Audit;

internal interface IAuditIntegrityProtector
{
    string Protect(ReadOnlySpan<byte> content);

    bool Verify(ReadOnlySpan<byte> content, string? integrityValue);
}
