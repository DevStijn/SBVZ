using System.Text;
using System.Xml.Linq;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class SbvzXmlProtocolTests
{
    private static readonly XNamespace Soap = SbvzConstants.SoapNamespace;
    private static readonly XNamespace Sbvz = SbvzConstants.XmlNamespace;

    [Fact]
    public void CreatesWsdlCompatibleLookupRequestWithoutEmptyElements()
    {
        var query = new SbvzPersonQuery(
            Bsn: null,
            GivenNames: null,
            Initial: null,
            SurnamePrefix: null,
            Surname: "Test-GG-Gevonden",
            BirthDate: "19700101",
            BirthPlace: null,
            BirthCountry: null,
            Sex: "M",
            Address: null);

        var document = SbvzXmlProtocol.CreateRequest(
            query,
            "01990f73-4963-7c51-a54f-83d482033731");

        Assert.Equal(Soap + "Envelope", document.Root?.Name);
        var operation = Assert.Single(document.Descendants(Sbvz + "OpvragenVerifieren"));
        Assert.Single(operation.Descendants(Sbvz + "Persoon"));
        Assert.Empty(operation.Descendants(Sbvz + "BSN"));
        Assert.Empty(operation.Descendants(Sbvz + "Voornamen"));
        Assert.Equal("Test-GG-Gevonden", Assert.Single(operation.Descendants(Sbvz + "Geslachtsnaam")).Value);
        Assert.Equal("19700101", Assert.Single(operation.Descendants(Sbvz + "Geboortedatum")).Value);
        Assert.Equal("M", Assert.Single(operation.Descendants(Sbvz + "Geslachtsaanduiding")).Value);
        Assert.Equal(
            "01990f73-4963-7c51-a54f-83d482033731",
            Assert.Single(operation.Descendants(Sbvz + "LokaalKenmerk")).Value);
    }

    [Fact]
    public void CreatesAllSupportedVerificationFieldsInWsdlOrder()
    {
        var query = new SbvzPersonQuery(
            Bsn: "078211529",
            GivenNames: "Jan Pieter",
            Initial: "J",
            SurnamePrefix: "van",
            Surname: "Test-GG-Gevonden",
            BirthDate: "19700101",
            BirthPlace: "Amsterdam",
            BirthCountry: "Nederland",
            Sex: "M",
            Address: new SbvzAddressQuery(
                Municipality: "Amsterdam",
                Street: "Teststraat",
                HouseNumber: "10",
                HouseLetter: "A",
                HouseNumberSuffix: "boven",
                HouseNumberDesignation: "by",
                PostalCode: "1234AB"));

        var document = SbvzXmlProtocol.CreateRequest(query, "local-reference");
        var personNames = Assert.Single(document.Descendants(Sbvz + "Persoon"))
            .Elements()
            .Select(element => element.Name.LocalName);
        var addressNames = Assert.Single(document.Descendants(Sbvz + "Adres"))
            .Elements()
            .Select(element => element.Name.LocalName);

        Assert.Equal(
            [
                "BSN",
                "Voornamen",
                "Voorletter",
                "VoorvoegselGeslachtsnaam",
                "Geslachtsnaam",
                "Geboortedatum",
                "Geboorteplaats",
                "Geboorteland",
                "Geslachtsaanduiding"
            ],
            personNames);
        Assert.Equal(
            [
                "GemeenteVanInschrijving",
                "Straatnaam",
                "Huisnummer",
                "Huisletter",
                "Huisnummertoevoeging",
                "AanduidingBijHuisnummer",
                "Postcode"
            ],
            addressNames);
    }

    [Fact]
    public void OmitsEmptyOptionalAddressObjectFromSoapRequest()
    {
        var query = new SbvzPersonQuery(
            null,
            null,
            null,
            null,
            "Test-GG-Gevonden",
            "19700101",
            null,
            null,
            "M",
            new SbvzAddressQuery(null, null, null, null, null, null, null));

        var document = SbvzXmlProtocol.CreateRequest(query, "local-reference");

        Assert.Empty(document.Descendants(Sbvz + "Adres"));
    }

    [Fact]
    public async Task ParsesCompleteOfficialAnswerShape()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <OpvragenVerifierenResponse xmlns="http://CIBG.SBV.Interface.XIS.Webservice/mrt21">
                  <OpvragenVerifierenAntwoordBericht>
                    <Vraag>
                      <Persoon>
                        <Geslachtsnaam>Test-GG-Gevonden</Geslachtsnaam>
                        <Geboortedatum>19700101</Geboortedatum>
                        <Geslachtsaanduiding>M</Geslachtsaanduiding>
                      </Persoon>
                    </Vraag>
                    <Antwoord>
                      <Persoon>
                        <BSN>078211529</BSN>
                        <Voornamen Afwijkend="false">Jan Pieter</Voornamen>
                        <Voorletter Afwijkend="false">J</Voorletter>
                        <AdellijkeTitelPredikaat>Jonkheer</AdellijkeTitelPredikaat>
                        <VoorvoegselGeslachtsnaam Afwijkend="false">van</VoorvoegselGeslachtsnaam>
                        <Geslachtsnaam Afwijkend="true">Test-GG-Gevonden</Geslachtsnaam>
                        <Geboortedatum Afwijkend="false">19700101</Geboortedatum>
                        <Geboorteplaats Afwijkend="false">Amsterdam</Geboorteplaats>
                        <Geboorteland Afwijkend="false">Nederland</Geboorteland>
                        <Geslachtsaanduiding Afwijkend="false">M</Geslachtsaanduiding>
                        <AanduidingGegevensInOnderzoekPersoon>Geboortedatum is in onderzoek</AanduidingGegevensInOnderzoekPersoon>
                        <DatumIngangOnderzoekPersoon>20240101</DatumIngangOnderzoekPersoon>
                      </Persoon>
                      <Adres>
                        <GemeenteVanInschrijving Afwijkend="false">Amsterdam</GemeenteVanInschrijving>
                        <FunctieAdres>Woonadres</FunctieAdres>
                        <Gemeentedeel>Centrum</Gemeentedeel>
                        <Straatnaam Afwijkend="true">Teststraat</Straatnaam>
                        <Huisnummer Afwijkend="false">10</Huisnummer>
                        <Huisletter Afwijkend="false">A</Huisletter>
                        <Huisnummertoevoeging Afwijkend="false">boven</Huisnummertoevoeging>
                        <AanduidingBijHuisnummer Afwijkend="false">by</AanduidingBijHuisnummer>
                        <Postcode Afwijkend="false">1234AB</Postcode>
                        <Locatiebeschrijving>Bij het park</Locatiebeschrijving>
                        <LandVanwaarIngeschreven>België</LandVanwaarIngeschreven>
                        <AanduidingGegevensInOnderzoekAdres>Postcode is in onderzoek</AanduidingGegevensInOnderzoekAdres>
                        <DatumIngangOnderzoekAdres>20240202</DatumIngangOnderzoekAdres>
                        <Woonplaatsnaam>Amsterdam</Woonplaatsnaam>
                        <Regel1AdresBuitenland>Voorbeeldlaan 1</Regel1AdresBuitenland>
                        <Regel2AdresBuitenland>1000 Brussel</Regel2AdresBuitenland>
                        <Regel3AdresBuitenland></Regel3AdresBuitenland>
                        <LandAdresBuitenland>België</LandAdresBuitenland>
                        <DatumAanvangAdresBuitenland>20230101</DatumAanvangAdresBuitenland>
                      </Adres>
                      <Inschrijving>
                        <OmschrijvingRedenOpschorting>Emigratie</OmschrijvingRedenOpschorting>
                        <IndicatieGeheim>Geen beperking</IndicatieGeheim>
                      </Inschrijving>
                      <Overlijden>
                        <DatumOverlijden>20250101</DatumOverlijden>
                        <AanduidingGegevensInOnderzoekOverlijden>Overlijden is in onderzoek</AanduidingGegevensInOnderzoekOverlijden>
                        <DatumIngangOnderzoekOverlijden>20250102</DatumIngangOnderzoekOverlijden>
                      </Overlijden>
                    </Antwoord>
                    <Resultaat>A</Resultaat>
                    <Melding Soort="G" Code="23002">BSN gevonden, maar met afwijkende gegevens</Melding>
                    <Melding Soort="W" Code="SX04">Voornamen voldoet niet aan het formaat A(200)</Melding>
                    <LokaalKenmerk>01990f73-4963-7c51-a54f-83d482033731</LokaalKenmerk>
                  </OpvragenVerifierenAntwoordBericht>
                </OpvragenVerifierenResponse>
              </soap:Body>
            </soap:Envelope>
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var response = await SbvzXmlProtocol.ParseResponseAsync(stream, CancellationToken.None);

        Assert.Equal("01990f73-4963-7c51-a54f-83d482033731", response.LocalReference);
        Assert.Equal(SbvzResult.GoodWithDifferences, response.Result);
        var answer = Assert.IsType<SbvzAnswer>(response.Answer);
        var person = Assert.IsType<SbvzPersonAnswer>(answer.Person);
        var address = Assert.IsType<SbvzAddressAnswer>(answer.Address);
        var registration = Assert.IsType<SbvzRegistrationAnswer>(answer.Registration);
        var death = Assert.IsType<SbvzDeathAnswer>(answer.Death);

        Assert.Equal("078211529", person.Bsn);
        Assert.Equal("Jan Pieter", person.GivenNames?.Value);
        Assert.False(person.GivenNames?.Deviates);
        Assert.True(person.Surname?.Deviates);
        Assert.Equal("Jonkheer", person.NobleTitleOrPredicate);
        Assert.Equal("20240101", person.Investigation?.StartDate);
        Assert.True(address.Street?.Deviates);
        Assert.Equal("Amsterdam", address.PlaceOfResidence);
        Assert.Equal("België", address.ForeignAddress?.Country);
        Assert.Null(address.ForeignAddress?.Line3);
        Assert.Equal("Emigratie", registration.SuspensionReason);
        Assert.Equal("Geen beperking", registration.DisclosureRestriction);
        Assert.Equal("20250101", death.Date);
        Assert.Equal("Overlijden is in onderzoek", death.Investigation?.Description);
        Assert.Collection(
            response.Messages,
            message => Assert.Equal("23002", message.Code),
            message =>
            {
                Assert.Equal(SbvzMessageType.Warning, message.Type);
                Assert.Equal("SX04", message.Code);
            });
    }

    [Fact]
    public async Task RejectsSoapFaultWithoutReturningFaultDetails()
    {
        const string xml = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <soap:Fault>
                  <faultcode>soap:Client</faultcode>
                  <faultstring>sensitive reflected input</faultstring>
                </soap:Fault>
              </soap:Body>
            </soap:Envelope>
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var exception = await Assert.ThrowsAsync<SbvzProtocolException>(
            () => SbvzXmlProtocol.ParseResponseAsync(stream, CancellationToken.None));

        Assert.Equal("SOAP fault: soap:Client", exception.Message);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptsFunctionalErrorWithoutIdentifyingAnswerData()
    {
        const string xml = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <OpvragenVerifierenResponse xmlns="http://CIBG.SBV.Interface.XIS.Webservice/mrt21">
                  <OpvragenVerifierenAntwoordBericht>
                    <Resultaat>F</Resultaat>
                    <Melding Soort="F" Code="23006">Vraag heeft niet tot één persoon geleid</Melding>
                    <LokaalKenmerk>local-reference</LokaalKenmerk>
                  </OpvragenVerifierenAntwoordBericht>
                </OpvragenVerifierenResponse>
              </soap:Body>
            </soap:Envelope>
            """;

        var response = await ParseAsync(xml);

        Assert.Equal(SbvzResult.Error, response.Result);
        Assert.Null(response.Answer);
        Assert.Equal("23006", Assert.Single(response.Messages).Code);
    }

    [Fact]
    public async Task RejectsSuccessfulResponseWithoutValidBsn()
    {
        const string xml = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <OpvragenVerifierenResponse xmlns="http://CIBG.SBV.Interface.XIS.Webservice/mrt21">
                  <OpvragenVerifierenAntwoordBericht>
                    <Antwoord><Persoon><BSN>123456789</BSN></Persoon></Antwoord>
                    <Resultaat>G</Resultaat>
                    <Melding Soort="G" Code="23002">BSN gevonden</Melding>
                    <LokaalKenmerk>local-reference</LokaalKenmerk>
                  </OpvragenVerifierenAntwoordBericht>
                </OpvragenVerifierenResponse>
              </soap:Body>
            </soap:Envelope>
            """;

        await Assert.ThrowsAsync<SbvzProtocolException>(() => ParseAsync(xml));
    }

    [Fact]
    public async Task RejectsComparedResponseValueWithoutRequiredDeviationAttribute()
    {
        const string xml = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <OpvragenVerifierenResponse xmlns="http://CIBG.SBV.Interface.XIS.Webservice/mrt21">
                  <OpvragenVerifierenAntwoordBericht>
                    <Antwoord>
                      <Persoon>
                        <BSN>078211529</BSN>
                        <Geslachtsnaam>Test</Geslachtsnaam>
                      </Persoon>
                    </Antwoord>
                    <Resultaat>G</Resultaat>
                    <Melding Soort="G" Code="23002">BSN gevonden</Melding>
                    <LokaalKenmerk>local-reference</LokaalKenmerk>
                  </OpvragenVerifierenAntwoordBericht>
                </OpvragenVerifierenResponse>
              </soap:Body>
            </soap:Envelope>
            """;

        await Assert.ThrowsAsync<SbvzProtocolException>(() => ParseAsync(xml));
    }

    [Fact]
    public async Task RejectsDuplicateSingletonResponseField()
    {
        const string xml = """
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                <OpvragenVerifierenResponse xmlns="http://CIBG.SBV.Interface.XIS.Webservice/mrt21">
                  <OpvragenVerifierenAntwoordBericht>
                    <Resultaat>F</Resultaat>
                    <Resultaat>F</Resultaat>
                    <Melding Soort="F" Code="23001">Geen resultaat gevonden</Melding>
                    <LokaalKenmerk>local-reference</LokaalKenmerk>
                  </OpvragenVerifierenAntwoordBericht>
                </OpvragenVerifierenResponse>
              </soap:Body>
            </soap:Envelope>
            """;

        await Assert.ThrowsAsync<SbvzProtocolException>(() => ParseAsync(xml));
    }

    [Theory]
    [InlineData("078211529")]
    [InlineData("123456782")]
    public void AcceptsBsnThatPassesElevenTest(string bsn)
    {
        var query = CreateVerificationQuery(bsn);

        SbvzQueryValidator.Validate(query);
    }

    [Theory]
    [InlineData("078211528")]
    [InlineData("123456789")]
    [InlineData("000000000")]
    [InlineData("12345678")]
    public void RejectsBsnThatFailsElevenTest(string bsn)
    {
        var query = CreateVerificationQuery(bsn);

        Assert.Throws<SbvzValidationException>(() => SbvzQueryValidator.Validate(query));
    }

    [Theory]
    [InlineData(null, null, "19700101", "M")]
    [InlineData("Test", null, null, "M")]
    [InlineData("Test", null, "19700101", "O")]
    [InlineData("Test", null, "18000101", "M")]
    [InlineData("Test", null, "29990101", "M")]
    public void RejectsInvalidSearchPathOrField(
        string? surname,
        string? postalCode,
        string? birthDate,
        string? sex)
    {
        var query = new SbvzPersonQuery(
            Bsn: null,
            GivenNames: null,
            Initial: null,
            SurnamePrefix: null,
            Surname: surname,
            BirthDate: birthDate,
            BirthPlace: null,
            BirthCountry: null,
            Sex: sex,
            Address: postalCode is null
                ? null
                : new SbvzAddressQuery(null, null, "1", null, null, null, postalCode));

        Assert.Throws<SbvzValidationException>(() => SbvzQueryValidator.Validate(query));
    }

    [Fact]
    public void AcceptsAddressSearchPathWithoutSurname()
    {
        var query = new SbvzPersonQuery(
            null,
            null,
            null,
            null,
            null,
            "19700101",
            null,
            null,
            "M",
            new SbvzAddressQuery(null, null, "10", null, null, null, "1234AB"));

        var path = SbvzQueryValidator.Validate(query);

        Assert.Equal(SbvzSearchPath.Address, path);
    }

    [Fact]
    public void RejectsInvalidRequiredPostalCodeOnAddressSearchPath()
    {
        var query = new SbvzPersonQuery(
            null,
            null,
            null,
            null,
            null,
            "19700101",
            null,
            null,
            "M",
            new SbvzAddressQuery(null, null, "10", null, null, null, "1234 AB"));

        Assert.Throws<SbvzValidationException>(() => SbvzQueryValidator.Validate(query));
    }

    [Fact]
    public void GivenNamesCannotReplaceRequiredSurname()
    {
        var query = new SbvzPersonQuery(
            null,
            "Jan",
            null,
            null,
            null,
            "19700101",
            null,
            null,
            "M",
            null);

        Assert.Throws<SbvzValidationException>(() => SbvzQueryValidator.Validate(query));
    }

    [Fact]
    public void SurnameSelectsSearchPathTwoAndAllowsInvalidOptionalAddressForSbvzWarning()
    {
        var query = new SbvzPersonQuery(
            null,
            new string('A', 201),
            "multiple",
            null,
            "Test",
            "19700101",
            null,
            null,
            "M",
            new SbvzAddressQuery(null, null, "too-long", "12", null, "invalid", "1234 AB"));

        var path = SbvzQueryValidator.Validate(query);

        Assert.Equal(SbvzSearchPath.Surname, path);
    }

    [Theory]
    [InlineData("19700101")]
    [InlineData("19700100")]
    [InlineData("19700000")]
    [InlineData("00000000")]
    public void AcceptsEveryOfficialBirthDatePrecision(string birthDate)
    {
        var query = new SbvzPersonQuery(
            null,
            null,
            null,
            null,
            "Test",
            birthDate,
            null,
            null,
            "M",
            null);

        SbvzQueryValidator.Validate(query);
    }

    [Fact]
    public void RejectsXmlControlCharactersBeforeCreatingSoap()
    {
        var query = new SbvzPersonQuery(
            null,
            null,
            null,
            null,
            "Test\0Name",
            "19700101",
            null,
            null,
            "M",
            null);

        Assert.Throws<SbvzValidationException>(() => SbvzXmlProtocol.CreateRequest(query, "local-reference"));
    }

    private static async Task<SbvzQueryResponse> ParseAsync(string xml)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        return await SbvzXmlProtocol.ParseResponseAsync(stream, CancellationToken.None);
    }

    private static SbvzPersonQuery CreateVerificationQuery(string bsn)
    {
        return new SbvzPersonQuery(
            bsn,
            null,
            null,
            null,
            "Test-GG-VerificatieGelukt",
            "19700101",
            null,
            null,
            "M",
            null);
    }
}
