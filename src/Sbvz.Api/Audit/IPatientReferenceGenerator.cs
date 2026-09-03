namespace Sbvz.Api.Audit;

public interface IPatientReferenceGenerator
{
    string CreateFromBsn(string bsn);
}
