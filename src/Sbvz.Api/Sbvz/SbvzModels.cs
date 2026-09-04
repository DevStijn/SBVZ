using System.Text.Json.Serialization;
using Sbvz.Api.Serialization;

namespace Sbvz.Api.Sbvz;

public sealed record SbvzPersonQuery(
    string? Bsn,
    string? GivenNames,
    string? Initial,
    string? SurnamePrefix,
    string? Surname,
    string? BirthDate,
    string? BirthPlace,
    string? BirthCountry,
    string? Sex,
    SbvzAddressQuery? Address);

public sealed record SbvzAddressQuery(
    string? Municipality,
    string? Street,
    string? HouseNumber,
    string? HouseLetter,
    string? HouseNumberSuffix,
    string? HouseNumberDesignation,
    string? PostalCode);

public sealed record SbvzQueryResponse(
    string LocalReference,
    SbvzResult Result,
    SbvzAnswer? Answer,
    IReadOnlyList<SbvzMessage> Messages);

public sealed record SbvzAnswer(
    SbvzPersonAnswer? Person,
    SbvzAddressAnswer? Address,
    SbvzRegistrationAnswer? Registration,
    SbvzDeathAnswer? Death);

public sealed record SbvzPersonAnswer(
    string? Bsn,
    SbvzComparedValue? GivenNames,
    SbvzComparedValue? Initial,
    string? NobleTitleOrPredicate,
    SbvzComparedValue? SurnamePrefix,
    SbvzComparedValue? Surname,
    SbvzComparedValue? BirthDate,
    SbvzComparedValue? BirthPlace,
    SbvzComparedValue? BirthCountry,
    SbvzComparedValue? Sex,
    IReadOnlyList<SbvzInvestigation> Investigations);

public sealed record SbvzAddressAnswer(
    SbvzComparedValue? Municipality,
    string? AddressFunction,
    string? MunicipalityPart,
    SbvzComparedValue? Street,
    SbvzComparedValue? HouseNumber,
    SbvzComparedValue? HouseLetter,
    SbvzComparedValue? HouseNumberSuffix,
    SbvzComparedValue? HouseNumberDesignation,
    SbvzComparedValue? PostalCode,
    string? PlaceOfResidence,
    string? LocationDescription,
    string? CountryFromWhichRegistered,
    IReadOnlyList<SbvzInvestigation> Investigations,
    SbvzForeignAddress? ForeignAddress);

public sealed record SbvzForeignAddress(
    string? Line1,
    string? Line2,
    string? Line3,
    string? Country,
    string? StartDate);

public sealed record SbvzRegistrationAnswer(
    string? SuspensionReason,
    string? DisclosureRestriction);

public sealed record SbvzDeathAnswer(
    string? Date,
    IReadOnlyList<SbvzInvestigation> Investigations);

public sealed record SbvzInvestigation(
    string? Description,
    string? StartDate);

public sealed record SbvzComparedValue(
    string Value,
    bool Deviates);

public sealed record SbvzMessage(
    SbvzMessageType Type,
    string Code,
    string Text);

[JsonConverter(typeof(StrictStringEnumConverter<SbvzResult>))]
public enum SbvzResult
{
    [JsonStringEnumMemberName("G")]
    Good,

    [JsonStringEnumMemberName("A")]
    GoodWithDifferences,

    [JsonStringEnumMemberName("F")]
    Error
}

[JsonConverter(typeof(StrictStringEnumConverter<SbvzMessageType>))]
public enum SbvzMessageType
{
    [JsonStringEnumMemberName("G")]
    Good,

    [JsonStringEnumMemberName("F")]
    Error,

    [JsonStringEnumMemberName("W")]
    Warning
}
