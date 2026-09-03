namespace Sbvz.Api.Sbvz;

internal static class SbvzConstants
{
    public const string HttpClientName = "sbvz";
    public const string XmlNamespace = "http://CIBG.SBV.Interface.XIS.Webservice/mrt21";
    public const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string SoapAction = XmlNamespace + "/OpvragenVerifieren";

    public static readonly Uri AcceptanceEndpoint = new(
        "https://webservice-acc.sbv-z.nl/cibg.sbv.interface.xis.webservice.mrt21/opvragenverifieren.asmx");

    public static readonly Uri ProductionEndpoint = new(
        "https://webservice.sbv-z.nl/cibg.sbv.interface.xis.webservice.mrt21/opvragenverifieren.asmx");
}
