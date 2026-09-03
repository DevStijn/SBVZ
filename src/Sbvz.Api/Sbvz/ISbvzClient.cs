namespace Sbvz.Api.Sbvz;

public interface ISbvzClient
{
    Task<SbvzQueryResponse> QueryAsync(
        SbvzPersonQuery query,
        string localReference,
        CancellationToken cancellationToken = default);
}
