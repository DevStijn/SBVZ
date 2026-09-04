using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNetEnv;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Sbvz.Api.Api;
using Sbvz.Api.Configuration;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.EndToEndTests;

public sealed class AcceptanceEndToEndTests
{
    private const string RvigBsn = "999990044";
    private static readonly ApiActor Actor = new("acceptance-test", "developer");
    private static readonly ApiAccessContext Access = new(
        Authorized: true,
        EmergencyAccess: false,
        TreatmentRelationship: true,
        Consent: true);
    private static readonly LookupScenario[] OfficialLookupScenarios =
    [
        new(
            "Test-GG-Gevonden",
            "19700101",
            BsnSex.Male,
            SbvzResult.Good,
            "078211529",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-Succes",
            "19800301",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "052950438",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-Overlijden",
            "19800201",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "504291051",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-Emigratie",
            "19800401",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "339827312",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-MinisterieelBesluit",
            "19800501",
            BsnSex.Female,
            SbvzResult.GoodWithDifferences,
            "171065670",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-RNI",
            "19800601",
            BsnSex.Female,
            SbvzResult.GoodWithDifferences,
            "114951470",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-IndicatieGeheim",
            "19800701",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "313659552",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-OnderzoekPersoon",
            "19800801",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "315995981",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-OnderzoekOverlijden",
            "19800901",
            BsnSex.Female,
            SbvzResult.GoodWithDifferences,
            "403996430",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AG-OnderzoekAdres",
            "19801001",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "525197321",
            [new(SbvzMessageType.Good, "23002")]),
        new(
            "Test-AW-Postcode",
            "19801101",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "009835994",
            [
                new(SbvzMessageType.Warning, "AF99"),
                new(SbvzMessageType.Good, "23002")
            ]),
        new(
            "Test-FF-Twee",
            "19801201",
            BsnSex.Male,
            SbvzResult.Error,
            null,
            [new(SbvzMessageType.Error, "2")]),
        new(
            "Test-FF-GeenResultaatGevonden",
            "19901101",
            BsnSex.Male,
            SbvzResult.Error,
            null,
            [new(SbvzMessageType.Error, "23001")]),
        new(
            "Test-FF-NietTotEenPersoon",
            "19901201",
            BsnSex.Male,
            SbvzResult.Error,
            null,
            [new(SbvzMessageType.Error, "23006")])
    ];
    private static readonly VerifyScenario[] OfficialVerifyScenarios =
    [
        new(
            "256297897",
            "Test-GG-VerificatieGelukt",
            "19700101",
            BsnSex.Male,
            SbvzResult.Good,
            "256297897",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "052950438",
            "Test-AG-Succes",
            "19800301",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "052950438",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "504291051",
            "Test-AG-Overlijden",
            "19800201",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "504291051",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "339827312",
            "Test-AG-Emigratie",
            "19800401",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "339827312",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "171065670",
            "Test-AG-MinistrieelBesluit",
            "19800501",
            BsnSex.Female,
            SbvzResult.GoodWithDifferences,
            "171065670",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "114951470",
            "Test-AG-RNI",
            "19800601",
            BsnSex.Female,
            SbvzResult.GoodWithDifferences,
            "114951470",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "313659552",
            "Test-AG-IndicatieGeheim",
            "19800701",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "313659552",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "315995981",
            "Test-AG-OnderzoekPersoon",
            "19800701",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "315995981",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "403996430",
            "Test-AG-OnderzoekOverlijden",
            "19800901",
            BsnSex.Female,
            SbvzResult.GoodWithDifferences,
            "403996430",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "525197321",
            "Test-AG-OnderzoekAdres",
            "19801001",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "525197321",
            [new(SbvzMessageType.Good, "2003")]),
        new(
            "009835994",
            "Test-AW-Postcode",
            "19801101",
            BsnSex.Male,
            SbvzResult.GoodWithDifferences,
            "009835994",
            [
                new(SbvzMessageType.Warning, "AF99"),
                new(SbvzMessageType.Good, "2003")
            ]),
        new(
            "668046508",
            "Test-FF-Twee",
            "19801201",
            BsnSex.Male,
            SbvzResult.Error,
            null,
            [new(SbvzMessageType.Error, "2")]),
        new(
            "182185783",
            "Test-FF-NietTotEenPersoon",
            "19901201",
            BsnSex.Male,
            SbvzResult.Error,
            null,
            [new(SbvzMessageType.Error, "2001")]),
        new(
            "106871997",
            "Test-FF-NummerIsGeenBSN",
            "19901101",
            BsnSex.Male,
            SbvzResult.Error,
            null,
            [new(SbvzMessageType.Error, "2002")])
    ];

    [Fact(Explicit = true)]
    [Trait("Category", "Acceptance")]
    public async Task ExercisesRvigSearchPathsAndOfficialScenarios()
    {
        LoadLocalEnvironment();
        Assert.True(
            string.Equals(
                Environment.GetEnvironmentVariable(SbvzOptions.ModeVariable),
                nameof(SbvzMode.Acceptance),
                StringComparison.OrdinalIgnoreCase),
            "Acceptance end-to-end tests are blocked unless SBVZ_MODE is Acceptance.");

        var apiKey = SecretValueResolver.Resolve(
            Environment.GetEnvironmentVariable(ApiAccessOptions.ApiKeyVariable),
            Environment.GetEnvironmentVariable(ApiAccessOptions.ApiKeyFileVariable));
        Assert.False(string.IsNullOrWhiteSpace(apiKey));

        using var application = new AcceptanceApplicationFactory();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey);
        var person = new BsnPersonInput(
            GivenNames: "Margriet",
            Surname: "Maassen",
            BirthDate: "19480117",
            Sex: BsnSex.Female);

        var surnameLookup = await LookupAsync(
            client,
            new BsnLookupRequest(
                Actor,
                Access,
                "acceptance-testing",
                person,
                "rvig-surname-path"));

        Assert.Equal(BsnSearchPath.Surname, surnameLookup.SearchPath);
        Assert.Equal(SbvzResult.Good, surnameLookup.Result);
        Assert.Equal(RvigBsn, surnameLookup.Answer?.Person?.Bsn);
        Assert.Contains(surnameLookup.Messages, message => message.Code == "23002");

        var addressLookup = await LookupAsync(
            client,
            new BsnLookupRequest(
                Actor,
                Access,
                "acceptance-testing",
                new BsnPersonInput(
                    BirthDate: "19480117",
                    Sex: BsnSex.Female),
                "rvig-address-path",
                new BsnAddressInput(
                    HouseNumber: "16",
                    PostalCode: "2252EB")));

        Assert.Equal(BsnSearchPath.Address, addressLookup.SearchPath);
        Assert.Equal(SbvzResult.Good, addressLookup.Result);
        Assert.Equal(RvigBsn, addressLookup.Answer?.Person?.Bsn);
        Assert.Contains(addressLookup.Messages, message => message.Code == "23002");

        var verification = await VerifyAsync(
            client,
            new BsnVerifyRequest(
                Actor,
                Access,
                "acceptance-testing",
                RvigBsn,
                person,
                "rvig-verification"));

        Assert.Equal(SbvzResult.Good, verification.Result);
        Assert.Equal(RvigBsn, verification.Answer?.Person?.Bsn);
        Assert.Contains(verification.Messages, message => message.Code == "2003");

        foreach (var scenario in OfficialLookupScenarios)
        {
            var response = await LookupAsync(
                client,
                new BsnLookupRequest(
                    Actor,
                    Access,
                    "acceptance-testing",
                    new BsnPersonInput(
                        Surname: scenario.Surname,
                        BirthDate: scenario.BirthDate,
                        Sex: scenario.Sex),
                    $"official-{scenario.Surname}"));

            Assert.Equal(scenario.Result, response.Result);
            Assert.Equal(scenario.Bsn, response.Answer?.Person?.Bsn);
            Assert.Equal(
                scenario.Messages,
                response.Messages.Select(message => new ExpectedMessage(
                    message.Type,
                    message.Code)));
        }

        foreach (var scenario in OfficialVerifyScenarios)
        {
            var response = await VerifyAsync(
                client,
                new BsnVerifyRequest(
                    Actor,
                    Access,
                    "acceptance-testing",
                    scenario.Bsn,
                    new BsnPersonInput(
                        Surname: scenario.Surname,
                        BirthDate: scenario.BirthDate,
                        Sex: scenario.Sex),
                    $"official-{scenario.Surname}"));

            Assert.Equal(scenario.Result, response.Result);
            Assert.Equal(scenario.ExpectedBsn, response.Answer?.Person?.Bsn);
            Assert.Equal(
                scenario.Messages,
                response.Messages.Select(message => new ExpectedMessage(
                    message.Type,
                    message.Code)));
        }
    }

    private static async Task<BsnOperationResponse> LookupAsync(
        HttpClient client,
        BsnLookupRequest request)
    {
        var response = await client.PostAsJsonAsync(
            "/v1/bsn/lookup",
            request,
            TestContext.Current.CancellationToken);
        var operation = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(
            cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(operation);

        return operation;
    }

    private static async Task<BsnOperationResponse> VerifyAsync(
        HttpClient client,
        BsnVerifyRequest request)
    {
        var response = await client.PostAsJsonAsync(
            "/v1/bsn/verify",
            request,
            TestContext.Current.CancellationToken);
        var operation = await response.Content.ReadFromJsonAsync<BsnOperationResponse>(
            cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(operation);

        return operation;
    }

    private static void LoadLocalEnvironment()
    {
        var environmentFile = LocalEnvironmentLoader.FindEnvironmentFile(
            Directory.GetCurrentDirectory());

        Assert.NotNull(environmentFile);
        Env.NoClobber().Load(environmentFile);
    }

    private sealed class AcceptanceApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
        }
    }

    private sealed record LookupScenario(
        string Surname,
        string BirthDate,
        BsnSex Sex,
        SbvzResult Result,
        string? Bsn,
        IReadOnlyList<ExpectedMessage> Messages);

    private sealed record VerifyScenario(
        string Bsn,
        string Surname,
        string BirthDate,
        BsnSex Sex,
        SbvzResult Result,
        string? ExpectedBsn,
        IReadOnlyList<ExpectedMessage> Messages);

    private sealed record ExpectedMessage(
        SbvzMessageType Type,
        string Code);
}
