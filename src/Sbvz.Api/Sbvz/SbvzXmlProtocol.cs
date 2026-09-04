using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Sbvz.Api.Sbvz;

internal static class SbvzXmlProtocol
{
    private const int MaximumInvestigationsPerCategory = 20;
    private const int MaximumMessages = 20;
    private static readonly XNamespace Soap = SbvzConstants.SoapNamespace;
    private static readonly XNamespace Sbvz = SbvzConstants.XmlNamespace;

    public static XDocument CreateRequest(SbvzPersonQuery query, string localReference)
    {
        SbvzQueryValidator.Validate(query);

        if (string.IsNullOrWhiteSpace(localReference) || localReference.Length > 50)
        {
            throw new SbvzValidationException(
                nameof(localReference),
                "Local reference must be non-blank and at most 50 characters.");
        }

        var person = new XElement(
            Sbvz + "Persoon",
            Optional("BSN", query.Bsn),
            Optional("Voornamen", query.GivenNames),
            Optional("Voorletter", query.Initial),
            Optional("VoorvoegselGeslachtsnaam", query.SurnamePrefix),
            Optional("Geslachtsnaam", query.Surname),
            Optional("Geboortedatum", query.BirthDate),
            Optional("Geboorteplaats", query.BirthPlace),
            Optional("Geboorteland", query.BirthCountry),
            Optional("Geslachtsaanduiding", query.Sex));
        var address = query.Address is null || !HasAnyValue(query.Address)
            ? null
            : new XElement(
                Sbvz + "Adres",
                Optional("GemeenteVanInschrijving", query.Address.Municipality),
                Optional("Straatnaam", query.Address.Street),
                Optional("Huisnummer", query.Address.HouseNumber),
                Optional("Huisletter", query.Address.HouseLetter),
                Optional("Huisnummertoevoeging", query.Address.HouseNumberSuffix),
                Optional("AanduidingBijHuisnummer", query.Address.HouseNumberDesignation),
                Optional("Postcode", query.Address.PostalCode));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XElement(
                    Soap + "Body",
                    new XElement(
                        Sbvz + "OpvragenVerifieren",
                        new XElement(
                            Sbvz + "OpvragenVerifierenVraagBericht",
                            new XElement(
                                Sbvz + "Vraag",
                                person,
                                address),
                            new XElement(Sbvz + "LokaalKenmerk", localReference))))));
    }

    public static async Task<SbvzQueryResponse> ParseResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_048_576
        };

        using var reader = XmlReader.Create(stream, settings);
        var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        var envelope = document.Root;

        if (envelope?.Name != Soap + "Envelope")
        {
            throw new SbvzProtocolException("SBV-Z response did not contain a SOAP 1.1 envelope.");
        }

        var body = RequiredElement(envelope, Soap + "Body", "SOAP body");
        var fault = OptionalElement(body, Soap + "Fault", "SOAP fault");

        if (fault is not null)
        {
            var faultCode = fault.Elements().FirstOrDefault(element => element.Name.LocalName == "faultcode")?.Value;
            throw new SbvzProtocolException(
                string.IsNullOrWhiteSpace(faultCode) ? "SOAP fault" : $"SOAP fault: {faultCode}");
        }

        var responseWrapper = RequiredElement(
            body,
            Sbvz + "OpvragenVerifierenResponse",
            "opvragen/verifiëren response");
        var responseMessage = RequiredElement(
            responseWrapper,
            Sbvz + "OpvragenVerifierenAntwoordBericht",
            "answer message");
        var resultCode = RequiredText(responseMessage, "Resultaat", 1);
        var localReference = RequiredText(responseMessage, "LokaalKenmerk", 50);
        var result = ParseResult(resultCode);
        var messages = ParseMessages(responseMessage);

        if ((result is SbvzResult.Error
                && messages.All(message => message.Type is not SbvzMessageType.Error))
            || (result is not SbvzResult.Error
                && (messages.Any(message => message.Type is SbvzMessageType.Error)
                    || messages.All(message => message.Type is not SbvzMessageType.Good))))
        {
            throw new SbvzProtocolException("SBV-Z response result and message types were inconsistent.");
        }

        var answerElement = OptionalElement(responseMessage, Sbvz + "Antwoord", "answer");
        var answer = answerElement is null ? null : ParseAnswer(answerElement);
        var responseBsn = answer?.Person?.Bsn;

        if ((result is SbvzResult.Good or SbvzResult.GoodWithDifferences)
            && (responseBsn is null || !SbvzQueryValidator.IsValidBsn(responseBsn)))
        {
            throw new SbvzProtocolException("Successful SBV-Z response did not contain a valid BSN.");
        }

        if (result is SbvzResult.Error && answerElement is not null)
        {
            throw new SbvzProtocolException("Failed SBV-Z response unexpectedly contained answer data.");
        }

        return new SbvzQueryResponse(
            localReference,
            result,
            answer,
            messages);
    }

    private static SbvzAnswer ParseAnswer(XElement answer)
    {
        var person = OptionalElement(answer, Sbvz + "Persoon", "person answer");
        var address = OptionalElement(answer, Sbvz + "Adres", "address answer");
        var registration = OptionalElement(answer, Sbvz + "Inschrijving", "registration answer");
        var death = OptionalElement(answer, Sbvz + "Overlijden", "death answer");

        return new SbvzAnswer(
            person is null ? null : ParsePerson(person),
            address is null ? null : ParseAddress(address),
            registration is null ? null : ParseRegistration(registration),
            death is null ? null : ParseDeath(death));
    }

    private static SbvzPersonAnswer ParsePerson(XElement person)
    {
        var bsn = OptionalText(person, "BSN", 9);

        if (bsn is not null && !SbvzQueryValidator.IsValidBsn(bsn))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid BSN.");
        }

        var birthDate = OptionalComparedValue(person, "Geboortedatum", 8);

        if (birthDate is { Value.Length: > 0 }
            && !SbvzQueryValidator.IsValidBirthDate(birthDate.Value))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid birth date.");
        }

        var sex = OptionalComparedValue(person, "Geslachtsaanduiding", 1);

        if (sex is { Value.Length: > 0 } && sex.Value is not ("M" or "V" or "O"))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid sex value.");
        }

        var initial = OptionalComparedValue(person, "Voorletter", 1);

        if (initial is { Value.Length: > 0 }
            && (initial.Value.Length != 1 || !char.IsLetter(initial.Value[0])))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid initial.");
        }

        return new SbvzPersonAnswer(
            bsn,
            OptionalComparedValue(person, "Voornamen", 200),
            initial,
            OptionalText(person, "AdellijkeTitelPredikaat", 10),
            OptionalComparedValue(person, "VoorvoegselGeslachtsnaam", 10),
            OptionalComparedValue(person, "Geslachtsnaam", 200),
            birthDate,
            OptionalComparedValue(person, "Geboorteplaats", 40),
            OptionalComparedValue(person, "Geboorteland", 40),
            sex,
            ParseInvestigations(
                person,
                "AanduidingGegevensInOnderzoekPersoon",
                "DatumIngangOnderzoekPersoon"));
    }

    private static SbvzAddressAnswer ParseAddress(XElement address)
    {
        var addressFunction = ParseAddressFunction(
            OptionalText(address, "FunctieAdres", 10));

        var houseLetter = OptionalComparedValue(address, "Huisletter", 1);

        if (houseLetter is { Value.Length: > 0 }
            && (houseLetter.Value.Length != 1 || !char.IsAsciiLetter(houseLetter.Value[0])))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid house letter.");
        }

        var houseNumberDesignation = ParseHouseNumberDesignation(address);

        var postalCode = OptionalComparedValue(address, "Postcode", 6);

        if (postalCode is { Value.Length: > 0 }
            && !SbvzQueryValidator.IsValidPostalCode(postalCode.Value))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid postal code.");
        }

        return new SbvzAddressAnswer(
            OptionalComparedValue(address, "GemeenteVanInschrijving", 40),
            addressFunction,
            OptionalText(address, "Gemeentedeel", 24),
            OptionalComparedValue(address, "Straatnaam", 40),
            OptionalComparedValue(address, "Huisnummer", 5),
            houseLetter,
            OptionalComparedValue(address, "Huisnummertoevoeging", 12),
            houseNumberDesignation,
            postalCode,
            OptionalText(address, "Woonplaatsnaam", 80),
            OptionalText(address, "Locatiebeschrijving", 35),
            OptionalText(address, "LandVanwaarIngeschreven", 40),
            ParseInvestigations(
                address,
                "AanduidingGegevensInOnderzoekAdres",
                "DatumIngangOnderzoekAdres"),
            ParseForeignAddress(address));
    }

    private static string? ParseAddressFunction(string? value)
    {
        return value switch
        {
            null => null,
            "Briefadres" or "briefadres" => "Briefadres",
            "Woonadres" or "woonadres" => "Woonadres",
            _ => throw new SbvzProtocolException(
                "SBV-Z response contained an invalid address function.")
        };
    }

    private static SbvzComparedValue? ParseHouseNumberDesignation(XElement address)
    {
        var value = OptionalComparedValue(address, "AanduidingBijHuisnummer", 3);

        return value?.Value switch
        {
            null or "" or "by" or "to" => value,
            "tot" => value with { Value = "to" },
            _ => throw new SbvzProtocolException(
                "SBV-Z response contained an invalid house number designation.")
        };
    }

    private static SbvzRegistrationAnswer ParseRegistration(XElement registration)
    {
        var suspensionReason = OptionalText(registration, "OmschrijvingRedenOpschorting", 33);
        var disclosureRestriction = OptionalText(registration, "IndicatieGeheim", 100);

        if (suspensionReason is not null
            && suspensionReason is not ("Overlijden"
                or "Emigratie"
                or "Ministerieel besluit"
                or "Persoonslijst aangelegd in de RNI"))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid suspension reason.");
        }

        if (disclosureRestriction is not null
            && disclosureRestriction is not ("Geen beperking"
                or "Er is een beperking op de gegevensverstrekking van toepassing"))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid disclosure restriction.");
        }

        return new SbvzRegistrationAnswer(
            suspensionReason,
            disclosureRestriction);
    }

    private static SbvzDeathAnswer ParseDeath(XElement death)
    {
        return new SbvzDeathAnswer(
            OptionalDate(death, "DatumOverlijden"),
            ParseInvestigations(
                death,
                "AanduidingGegevensInOnderzoekOverlijden",
                "DatumIngangOnderzoekOverlijden"));
    }

    private static SbvzForeignAddress? ParseForeignAddress(XElement address)
    {
        var line1 = OptionalText(address, "Regel1AdresBuitenland", 38);
        var line2 = OptionalText(address, "Regel2AdresBuitenland", 38);
        var line3 = OptionalText(address, "Regel3AdresBuitenland", 38);
        var country = OptionalText(address, "LandAdresBuitenland", 40);
        var startDate = OptionalDate(address, "DatumAanvangAdresBuitenland");

        return line1 is null && line2 is null && line3 is null && country is null && startDate is null
            ? null
            : new SbvzForeignAddress(line1, line2, line3, country, startDate);
    }

    private static SbvzInvestigation[] ParseInvestigations(
        XElement parent,
        string descriptionName,
        string startDateName)
    {
        var descriptions = parent
            .Elements(Sbvz + descriptionName)
            .Take(MaximumInvestigationsPerCategory + 1)
            .ToArray();
        var startDates = parent
            .Elements(Sbvz + startDateName)
            .Take(MaximumInvestigationsPerCategory + 1)
            .ToArray();

        if (descriptions.Length > MaximumInvestigationsPerCategory
            || startDates.Length > MaximumInvestigationsPerCategory)
        {
            throw new SbvzProtocolException(
                $"SBV-Z response contained too many {descriptionName} values.");
        }

        return [.. Enumerable
            .Range(0, Math.Max(descriptions.Length, startDates.Length))
            .Select(index => new SbvzInvestigation(
                index < descriptions.Length
                    ? ReadOptionalText(descriptions[index], descriptionName, 50)
                    : null,
                index < startDates.Length
                    ? ReadOptionalDate(startDates[index], startDateName)
                    : null))
            .Where(investigation => investigation.Description is not null
                || investigation.StartDate is not null)];
    }

    private static SbvzMessage[] ParseMessages(XElement responseMessage)
    {
        var messages = responseMessage
            .Elements(Sbvz + "Melding")
            .Take(MaximumMessages + 1)
            .Select(ParseMessage)
            .ToArray();

        if (messages.Length == 0 || messages.Length > MaximumMessages)
        {
            throw new SbvzProtocolException(
                messages.Length == 0
                    ? "SBV-Z response did not contain a result message."
                    : "SBV-Z response contained too many result messages.");
        }

        return messages;
    }

    private static SbvzMessage ParseMessage(XElement element)
    {
        var type = element.Attribute("Soort")?.Value;
        var code = element.Attribute("Code")?.Value;
        var text = element.Value;

        if (string.IsNullOrWhiteSpace(code)
            || code.Length > 5
            || !code.All(char.IsAsciiLetterOrDigit))
        {
            throw new SbvzProtocolException("SBV-Z response contained an invalid message code.");
        }

        if (string.IsNullOrWhiteSpace(text) || text.Length > 350)
        {
            throw new SbvzProtocolException("SBV-Z response contained invalid message text.");
        }

        return new SbvzMessage(ParseMessageType(type), code, text);
    }

    private static SbvzComparedValue? OptionalComparedValue(
        XElement parent,
        string localName,
        int maximumLength)
    {
        var element = OptionalElement(parent, Sbvz + localName, localName);

        if (element is null)
        {
            return null;
        }

        if (element.Value.Length > maximumLength)
        {
            throw new SbvzProtocolException($"SBV-Z response field {localName} exceeded its maximum length.");
        }

        var deviationAttribute = element.Attribute("Afwijkend")?.Value
            ?? throw new SbvzProtocolException($"SBV-Z response field {localName} did not contain Afwijkend.");

        try
        {
            return new SbvzComparedValue(element.Value, XmlConvert.ToBoolean(deviationAttribute));
        }
        catch (FormatException exception)
        {
            throw new SbvzProtocolException(
                $"SBV-Z response field {localName} contained an invalid Afwijkend value.",
                exception);
        }
    }

    private static string? OptionalText(XElement parent, string localName, int maximumLength)
    {
        var element = OptionalElement(parent, Sbvz + localName, localName);

        if (element is null)
        {
            return null;
        }

        if (element.Value.Length > maximumLength)
        {
            throw new SbvzProtocolException($"SBV-Z response field {localName} exceeded its maximum length.");
        }

        return element.Value.Length == 0 ? null : element.Value;
    }

    private static string? OptionalDate(XElement parent, string localName)
    {
        var value = OptionalText(parent, localName, 8);

        if (!string.IsNullOrEmpty(value)
            && !DateOnly.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new SbvzProtocolException($"SBV-Z response field {localName} contained an invalid date.");
        }

        return value;
    }

    private static string? ReadOptionalDate(XElement element, string localName)
    {
        var value = ReadOptionalText(element, localName, 8);

        if (!string.IsNullOrEmpty(value)
            && !DateOnly.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new SbvzProtocolException($"SBV-Z response field {localName} contained an invalid date.");
        }

        return value;
    }

    private static string? ReadOptionalText(
        XElement element,
        string localName,
        int maximumLength)
    {
        if (element.Value.Length > maximumLength)
        {
            throw new SbvzProtocolException($"SBV-Z response field {localName} exceeded its maximum length.");
        }

        return element.Value.Length == 0 ? null : element.Value;
    }

    private static string RequiredText(XElement parent, string localName, int maximumLength)
    {
        var value = OptionalText(parent, localName, maximumLength);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SbvzProtocolException($"SBV-Z response did not contain a valid {localName} value.");
        }

        return value;
    }

    private static XElement RequiredElement(XElement parent, XName name, string description)
    {
        return OptionalElement(parent, name, description)
            ?? throw new SbvzProtocolException($"SBV-Z response did not contain the {description}.");
    }

    private static XElement? OptionalElement(XElement parent, XName name, string description)
    {
        var elements = parent.Elements(name).Take(2).ToArray();

        if (elements.Length > 1)
        {
            throw new SbvzProtocolException($"SBV-Z response contained multiple {description} elements.");
        }

        return elements.SingleOrDefault();
    }

    private static XElement? Optional(string name, string? value)
    {
        return value is null ? null : new XElement(Sbvz + name, value);
    }

    private static bool HasAnyValue(SbvzAddressQuery address)
    {
        return address.Municipality is not null
            || address.Street is not null
            || address.HouseNumber is not null
            || address.HouseLetter is not null
            || address.HouseNumberSuffix is not null
            || address.HouseNumberDesignation is not null
            || address.PostalCode is not null;
    }

    private static SbvzResult ParseResult(string resultCode)
    {
        return resultCode switch
        {
            "G" => SbvzResult.Good,
            "A" => SbvzResult.GoodWithDifferences,
            "F" => SbvzResult.Error,
            _ => throw new SbvzProtocolException("SBV-Z returned an unknown result code.")
        };
    }

    private static SbvzMessageType ParseMessageType(string? messageType)
    {
        return messageType switch
        {
            "G" => SbvzMessageType.Good,
            "F" => SbvzMessageType.Error,
            "W" => SbvzMessageType.Warning,
            _ => throw new SbvzProtocolException("SBV-Z response contained an invalid message type.")
        };
    }
}

public sealed class SbvzProtocolException : Exception
{
    public SbvzProtocolException(string message)
        : base(message)
    {
    }

    public SbvzProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
