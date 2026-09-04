using Sbvz.Api.Sbvz;

namespace Sbvz.Api.Tests;

internal sealed class FictionalSbvzClient : ISbvzClient
{
    private const string TestBsn = "078211529";
    private const string TestSurname = "Test-GG-Gevonden";
    private const string TestBirthDate = "19700101";
    private const string TestSex = "M";
    private const string TestPostalCode = "1234AB";
    private const string TestHouseNumber = "10";

    public Task<SbvzQueryResponse> QueryAsync(
        SbvzPersonQuery query,
        string localReference,
        CancellationToken cancellationToken = default)
    {
        SbvzQueryValidator.Validate(query);

        var matchesSearchPath = query.Surname is null
            ? query.Address?.PostalCode == TestPostalCode
                && query.Address.HouseNumber == TestHouseNumber
            : query.Surname == TestSurname;
        var matches = (query.Bsn is null || query.Bsn == TestBsn)
            && matchesSearchPath
            && query.BirthDate == TestBirthDate
            && query.Sex == TestSex;
        var response = matches
            ? new SbvzQueryResponse(
                localReference,
                SbvzResult.Good,
                CreateAnswer(),
                [new SbvzMessage(
                    SbvzMessageType.Good,
                    query.Bsn is null ? "23002" : "2003",
                    query.Bsn is null ? "BSN gevonden" : "Verificatie gelukt")])
            : new SbvzQueryResponse(
                localReference,
                SbvzResult.Error,
                null,
                [new SbvzMessage(
                    SbvzMessageType.Error,
                    query.Bsn is null ? "23001" : "2001",
                    query.Bsn is null
                        ? "Geen resultaat gevonden"
                        : "Vraag heeft niet tot één persoon geleid")]);

        return Task.FromResult(response);
    }

    private static SbvzAnswer CreateAnswer()
    {
        return new SbvzAnswer(
            new SbvzPersonAnswer(
                TestBsn,
                new SbvzComparedValue("Jan", false),
                null,
                null,
                null,
                new SbvzComparedValue(TestSurname, false),
                new SbvzComparedValue(TestBirthDate, false),
                new SbvzComparedValue("Amsterdam", false),
                new SbvzComparedValue("Nederland", false),
                new SbvzComparedValue(TestSex, false),
                []),
            new SbvzAddressAnswer(
                new SbvzComparedValue("Amsterdam", false),
                "Woonadres",
                "Amsterdam",
                new SbvzComparedValue("Teststraat", false),
                new SbvzComparedValue(TestHouseNumber, false),
                new SbvzComparedValue(string.Empty, false),
                new SbvzComparedValue(string.Empty, false),
                new SbvzComparedValue(string.Empty, false),
                new SbvzComparedValue(TestPostalCode, false),
                "Amsterdam",
                string.Empty,
                string.Empty,
                [],
                null),
            new SbvzRegistrationAnswer(null, "Geen beperking"),
            null);
    }
}
