using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Sbvz.Api.Sbvz;

internal static partial class SbvzQueryValidator
{
    private const int MaximumTransportValueLength = 4_096;

    public static SbvzSearchPath Validate(SbvzPersonQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        ValidateProvidedValue(query.Bsn, "bsn");
        ValidateProvidedValue(query.GivenNames, "person.givenNames");
        ValidateProvidedValue(query.Initial, "person.initial");
        ValidateProvidedValue(query.SurnamePrefix, "person.surnamePrefix");
        ValidateProvidedValue(query.Surname, "person.surname");
        ValidateProvidedValue(query.BirthDate, "person.birthDate");
        ValidateProvidedValue(query.BirthPlace, "person.birthPlace");
        ValidateProvidedValue(query.BirthCountry, "person.birthCountry");
        ValidateProvidedValue(query.Sex, "person.sex");
        ValidateAddressValues(query.Address);

        ValidateMaximumLength(query.GivenNames, 200, "person.givenNames");
        ValidateSingleLetter(query.Initial, "person.initial");
        ValidateMaximumLength(query.SurnamePrefix, 10, "person.surnamePrefix");
        ValidateMaximumLength(query.Surname, 200, "person.surname");
        ValidateMaximumLength(query.BirthPlace, 40, "person.birthPlace");
        ValidateMaximumLength(query.BirthCountry, 40, "person.birthCountry");

        if (query.Bsn is not null && !IsValidBsn(query.Bsn))
        {
            throw new SbvzValidationException("bsn", "BSN must contain nine digits and pass the eleven test.");
        }

        if (query.BirthDate is null)
        {
            throw new SbvzValidationException(
                "person.birthDate",
                "Birth date is required for both SBV-Z search paths.");
        }

        if (!IsValidBirthDate(query.BirthDate))
        {
            throw new SbvzValidationException(
                "person.birthDate",
                "Birth date must use yyyyMMdd, yyyyMM00, yyyy0000 or 00000000, be in the past and not be more than 150 years ago.");
        }

        if (query.Sex is not ("M" or "V"))
        {
            throw new SbvzValidationException("person.sex", "Sex is required and must be M or V.");
        }

        if (query.Surname is not null)
        {
            return SbvzSearchPath.Surname;
        }

        if (query.Address?.PostalCode is null || query.Address.HouseNumber is null)
        {
            throw new SbvzValidationException(
                "address",
                "Without a surname, postal code and house number are both required for SBV-Z search path 1.");
        }

        return SbvzSearchPath.Address;
    }

    private static void ValidateAddressValues(SbvzAddressQuery? address)
    {
        if (address is null)
        {
            return;
        }

        ValidateProvidedValue(address.Municipality, "address.municipality");
        ValidateProvidedValue(address.Street, "address.street");
        ValidateProvidedValue(address.HouseNumber, "address.houseNumber");
        ValidateProvidedValue(address.HouseLetter, "address.houseLetter");
        ValidateProvidedValue(address.HouseNumberSuffix, "address.houseNumberSuffix");
        ValidateProvidedValue(address.HouseNumberDesignation, "address.houseNumberDesignation");
        ValidateProvidedValue(address.PostalCode, "address.postalCode");

        ValidateMaximumLength(address.Municipality, 40, "address.municipality");
        ValidateMaximumLength(address.Street, 40, "address.street");
        ValidateMaximumLength(address.HouseNumber, 5, "address.houseNumber");
        ValidateSingleAsciiLetter(address.HouseLetter, "address.houseLetter");
        ValidateMaximumLength(address.HouseNumberSuffix, 12, "address.houseNumberSuffix");

        if (address.HouseNumberDesignation is not null
            && address.HouseNumberDesignation is not ("by" or "to"))
        {
            throw new SbvzValidationException(
                "address.houseNumberDesignation",
                "House number designation must be by or to.");
        }

        if (address.PostalCode is not null && !IsValidPostalCode(address.PostalCode))
        {
            throw new SbvzValidationException(
                "address.postalCode",
                "Postal code must use format 9999XX without a space.");
        }
    }

    private static void ValidateProvidedValue(string? value, string field)
    {
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SbvzValidationException(field, "Value must not be blank when provided.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new SbvzValidationException(
                field,
                "Value must not contain leading, trailing or control characters.");
        }

        if (value.Length > MaximumTransportValueLength)
        {
            throw new SbvzValidationException(
                field,
                $"Value must contain at most {MaximumTransportValueLength} characters.");
        }

        try
        {
            XmlConvert.VerifyXmlChars(value);
        }
        catch (XmlException)
        {
            throw new SbvzValidationException(field, "Value contains a character that cannot be represented in XML.");
        }
    }

    private static void ValidateMaximumLength(string? value, int maximumLength, string field)
    {
        if (value is not null && value.Length > maximumLength)
        {
            throw new SbvzValidationException(field, $"Value must contain at most {maximumLength} characters.");
        }
    }

    private static void ValidateSingleLetter(string? value, string field)
    {
        if (value is not null && (value.Length != 1 || !char.IsLetter(value[0])))
        {
            throw new SbvzValidationException(field, "Value must contain one letter.");
        }
    }

    private static void ValidateSingleAsciiLetter(string? value, string field)
    {
        if (value is not null && (value.Length != 1 || !char.IsAsciiLetter(value[0])))
        {
            throw new SbvzValidationException(field, "Value must contain one ASCII letter.");
        }
    }

    internal static bool IsValidBsn(string bsn)
    {
        if (bsn.Length != 9 || !bsn.All(char.IsAsciiDigit) || bsn.All(character => character == '0'))
        {
            return false;
        }

        var checksum = 0;

        for (var index = 0; index < 8; index++)
        {
            checksum += (bsn[index] - '0') * (9 - index);
        }

        checksum -= bsn[8] - '0';

        return checksum % 11 == 0;
    }

    internal static bool IsValidBirthDate(string value)
    {
        if (value.Length != 8 || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (value == "00000000")
        {
            return true;
        }

        if (!int.TryParse(value.AsSpan(0, 4), out var year) || year == 0)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (year < today.Year - 150 || year > today.Year)
        {
            return false;
        }

        var month = value.AsSpan(4, 2);
        var day = value.AsSpan(6, 2);

        if (month.SequenceEqual("00") && day.SequenceEqual("00"))
        {
            return true;
        }

        if (!int.TryParse(month, out var parsedMonth) || parsedMonth is < 1 or > 12)
        {
            return false;
        }

        if (day.SequenceEqual("00"))
        {
            return year < today.Year || parsedMonth <= today.Month;
        }

        return DateOnly.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var birthDate)
            && birthDate < today
            && birthDate >= today.AddYears(-150);
    }

    internal static bool IsValidPostalCode(string value) => PostalCodePattern().IsMatch(value);

    [GeneratedRegex("^[0-9]{4}[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex PostalCodePattern();
}

internal enum SbvzSearchPath
{
    Address,
    Surname
}

public sealed class SbvzValidationException(string field, string message) : ArgumentException(message, field)
{
    public string Field { get; } = field;
}
