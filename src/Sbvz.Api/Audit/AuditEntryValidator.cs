namespace Sbvz.Api.Audit;

internal static class AuditEntryValidator
{
    public static void Validate(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(entry.Actor);
        ArgumentNullException.ThrowIfNull(entry.Access);
        ArgumentNullException.ThrowIfNull(entry.Operation);
        ArgumentNullException.ThrowIfNull(entry.Exchange);

        if (entry.SchemaVersion != AuditEntry.CurrentSchemaVersion)
        {
            throw new ArgumentException("Unsupported audit schema version.", nameof(entry));
        }

        if (entry.EventId == Guid.Empty)
        {
            throw new ArgumentException("Event ID must be set.", nameof(entry));
        }

        if (entry.RegisteredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Registration time must use UTC.", nameof(entry));
        }

        if (entry.OperationStartedAtUtc.Offset != TimeSpan.Zero
            || entry.OperationStartedAtUtc > entry.RegisteredAtUtc)
        {
            throw new ArgumentException(
                "Operation start time must use UTC and must not follow registration time.",
                nameof(entry));
        }

        RequireValue(entry.OperationId, 36, nameof(entry.OperationId));

        if (!Guid.TryParseExact(entry.OperationId, "D", out _))
        {
            throw new ArgumentException("Operation ID must be a canonical UUID.", nameof(entry));
        }

        RequireOptionalValue(entry.TraceId, 128, nameof(entry.TraceId));
        RequireValue(entry.SubscriberNumber, 8, nameof(entry.SubscriberNumber));

        if (entry.SubscriberNumber.Length != 8 || !entry.SubscriberNumber.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Subscriber number must contain eight digits.", nameof(entry));
        }

        RequireOptionalValue(entry.RecordId, 100, nameof(entry.RecordId));
        RequireOptionalSafeIdentifier(entry.ApiClientId, 100, nameof(entry.ApiClientId));
        RequireValue(entry.Operation.Name, 100, nameof(entry.Operation.Name));
        RequireValue(entry.Operation.Purpose, 100, nameof(entry.Operation.Purpose));
        RequireValue(entry.Actor.Id, 100, nameof(entry.Actor.Id));
        RequireValue(entry.Actor.Role, 100, nameof(entry.Actor.Role));
        RequireOptionalValue(entry.Exchange.ResponseCode, 256, nameof(entry.Exchange.ResponseCode));

        if (entry.PatientReference is not null
            && !IsValidPatientReference(entry.PatientReference))
        {
            throw new ArgumentException(
                "Patient reference must be generated with HMAC-SHA256.",
                nameof(entry));
        }

        if (entry.Exchange.DurationMilliseconds < 0)
        {
            throw new ArgumentException("Duration cannot be negative.", nameof(entry));
        }

        if (entry.Operation.DataCategory is AuditDataCategory.PatientIdentification
            && entry.ApiClientId is null)
        {
            throw new ArgumentException(
                "API client ID is required for patient identification operations.",
                nameof(entry));
        }
    }

    private static bool IsValidPatientReference(string value)
    {
        const string prefix = "hmac-sha256:";

        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var keySeparator = value.IndexOf(':', prefix.Length);

        if (keySeparator <= prefix.Length || value.Length != keySeparator + 1 + 64)
        {
            return false;
        }

        var keyId = value.AsSpan(prefix.Length, keySeparator - prefix.Length);
        var hash = value.AsSpan(keySeparator + 1);

        foreach (var character in keyId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        foreach (var character in hash)
        {
            if (!IsLowerHex(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerHex(char character)
    {
        return char.IsAsciiDigit(character) || character is >= 'a' and <= 'f';
    }

    private static void RequireValue(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{name} contains an invalid value.", name);
        }
    }

    private static void RequireOptionalValue(string? value, int maximumLength, string name)
    {
        if (value is not null)
        {
            RequireValue(value, maximumLength, name);
        }
    }

    private static void RequireOptionalSafeIdentifier(
        string? value,
        int maximumLength,
        string name)
    {
        RequireOptionalValue(value, maximumLength, name);

        if (value is not null
            && value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or '-')))
        {
            throw new ArgumentException($"{name} contains an invalid value.", name);
        }
    }
}
