namespace Sbvz.Api.Audit;

internal sealed class AuditStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);
