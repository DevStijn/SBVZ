using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Sbvz.Api.Sbvz;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class SbvzXmlClientTests
{
    [Fact]
    public async Task UsesOfficialAcceptanceEndpointAndSoap11Action()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new SbvzXmlClient(
            new StaticHttpClientFactory(httpClient),
            Options.Create(
                new SbvzOptions
                {
                    Mode = nameof(SbvzMode.Acceptance),
                    SubscriberNumber = "12345678"
                }));
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
            null);

        var response = await client.QueryAsync(
            query,
            "01990f73-4963-7c51-a54f-83d482033731");

        Assert.Equal(SbvzResult.Good, response.Result);
        Assert.Equal(SbvzConstants.AcceptanceEndpoint, handler.RequestUri);
        Assert.Equal("text/xml", handler.ContentType);
        Assert.Equal("utf-8", handler.CharSet);
        Assert.Equal($"\"{SbvzConstants.SoapAction}\"", handler.SoapAction);
        Assert.Contains("<OpvragenVerifieren", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains(
            "<LokaalKenmerk>01990f73-4963-7c51-a54f-83d482033731</LokaalKenmerk>",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<BSN>", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsResponseWithDifferentLocalReference()
    {
        var handler = new RecordingHandler
        {
            ResponseLocalReference = "01990f73-4963-7c51-a54f-83d482033732"
        };
        using var httpClient = new HttpClient(handler);
        var client = new SbvzXmlClient(
            new StaticHttpClientFactory(httpClient),
            Options.Create(
                new SbvzOptions
                {
                    Mode = nameof(SbvzMode.Acceptance),
                    SubscriberNumber = "12345678"
                }));
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
            null);

        var exception = await Assert.ThrowsAsync<SbvzProtocolException>(
            () => client.QueryAsync(
                query,
                "01990f73-4963-7c51-a54f-83d482033731"));

        Assert.Equal("SBV-Z returned an unexpected local reference.", exception.Message);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string ResponseLocalReference { get; init; } = "01990f73-4963-7c51-a54f-83d482033731";
        public Uri? RequestUri { get; private set; }
        public string? ContentType { get; private set; }
        public string? CharSet { get; private set; }
        public string? SoapAction { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            CharSet = request.Content?.Headers.ContentType?.CharSet;
            SoapAction = request.Headers.GetValues("SOAPAction").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            var response = $"""
                <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
                  <soap:Body>
                    <OpvragenVerifierenResponse xmlns="http://CIBG.SBV.Interface.XIS.Webservice/mrt21">
                      <OpvragenVerifierenAntwoordBericht>
                        <Antwoord><Persoon><BSN>078211529</BSN></Persoon></Antwoord>
                        <Resultaat>G</Resultaat>
                        <Melding Soort="G" Code="23002">BSN gevonden</Melding>
                        <LokaalKenmerk>{ResponseLocalReference}</LokaalKenmerk>
                      </OpvragenVerifierenAntwoordBericht>
                    </OpvragenVerifierenResponse>
                  </soap:Body>
                </soap:Envelope>
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "text/xml")
            };
        }
    }
}
