using System.Text.Json.Serialization;
using Sbvz.Api.Sbvz;
using Sbvz.Api.Serialization;

namespace Sbvz.Api.Api;

public sealed record BsnLookupRequest(
    [property: JsonRequired] ApiActor Actor,
    [property: JsonRequired] ApiAccessContext Access,
    [property: JsonRequired] string Purpose,
    [property: JsonRequired] BsnPersonInput Person,
    string? RecordId = null,
    BsnAddressInput? Address = null);

public sealed record BsnVerifyRequest(
    [property: JsonRequired] ApiActor Actor,
    [property: JsonRequired] ApiAccessContext Access,
    [property: JsonRequired] string Purpose,
    [property: JsonRequired] string Bsn,
    [property: JsonRequired] BsnPersonInput Person,
    string? RecordId = null,
    BsnAddressInput? Address = null);

public sealed record ApiActor(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Role);

public sealed record ApiAccessContext(
    [property: JsonRequired] bool EmergencyAccess,
    bool? TreatmentRelationship = null,
    bool? Consent = null);

public sealed record BsnPersonInput(
    string? GivenNames = null,
    string? Initial = null,
    string? SurnamePrefix = null,
    string? Surname = null,
    string? BirthDate = null,
    string? BirthPlace = null,
    string? BirthCountry = null,
    BsnSex? Sex = null);

public sealed record BsnAddressInput(
    string? Municipality = null,
    string? Street = null,
    string? HouseNumber = null,
    string? HouseLetter = null,
    string? HouseNumberSuffix = null,
    string? HouseNumberDesignation = null,
    string? PostalCode = null);

[JsonConverter(typeof(StrictStringEnumConverter<BsnSex>))]
public enum BsnSex
{
    [JsonStringEnumMemberName("M")]
    Male,

    [JsonStringEnumMemberName("V")]
    Female
}

public sealed record BsnOperationResponse(
    Guid OperationId,
    BsnSearchPath SearchPath,
    SbvzResult Result,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] SbvzAnswer? Answer,
    IReadOnlyList<SbvzMessage> Messages);

[JsonConverter(typeof(StrictStringEnumConverter<BsnSearchPath>))]
public enum BsnSearchPath
{
    [JsonStringEnumMemberName("address")]
    Address,

    [JsonStringEnumMemberName("surname")]
    Surname
}
