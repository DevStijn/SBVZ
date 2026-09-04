using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Options;

namespace Sbvz.Api.Sbvz;

internal sealed class SbvzXmlClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SbvzOptions> options) : ISbvzClient
{
    public async Task<SbvzQueryResponse> QueryAsync(
        SbvzPersonQuery query,
        string localReference,
        CancellationToken cancellationToken = default)
    {
        var requestDocument = SbvzXmlProtocol.CreateRequest(query, localReference);
        var endpoint = GetEndpoint(options.Value.Mode);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        using var contentStream = new MemoryStream();

        await using (var writer = XmlWriter.Create(
            contentStream,
            new XmlWriterSettings
            {
                Async = true,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = false,
                CloseOutput = false
            }))
        {
            await requestDocument.WriteToAsync(writer, cancellationToken);
        }

        contentStream.Position = 0;
        request.Content = new StreamContent(contentStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml")
        {
            CharSet = "utf-8"
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{SbvzConstants.SoapAction}\"");

        var client = httpClientFactory.CreateClient(SbvzConstants.HttpClientName);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            var result = await SbvzXmlProtocol.ParseResponseAsync(responseStream, cancellationToken);

            if (!string.Equals(result.LocalReference, localReference, StringComparison.Ordinal))
            {
                throw new SbvzProtocolException("SBV-Z returned an unexpected local reference.");
            }

            if (query.Bsn is not null
                && result.Result is SbvzResult.Good or SbvzResult.GoodWithDifferences
                && !string.Equals(
                    result.Answer?.Person?.Bsn,
                    query.Bsn,
                    StringComparison.Ordinal))
            {
                throw new SbvzProtocolException("SBV-Z returned a different BSN for a verification request.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SbvzProtocolException($"SBV-Z returned HTTP {(int)response.StatusCode}.");
            }

            return result;
        }
        catch (XmlException exception)
        {
            throw new SbvzProtocolException("SBV-Z returned invalid XML.", exception);
        }
    }

    private static Uri GetEndpoint(string configuredMode)
    {
        return Enum.Parse<SbvzMode>(configuredMode, ignoreCase: true) switch
        {
            SbvzMode.Acceptance => SbvzConstants.AcceptanceEndpoint,
            SbvzMode.Production => SbvzConstants.ProductionEndpoint,
            _ => throw new InvalidOperationException("The XML client cannot run in mock mode.")
        };
    }
}
