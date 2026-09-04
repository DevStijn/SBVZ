namespace Sbvz.Api.Safety;

internal sealed class EmergencyStopOptions
{
    public const string ObjectKeyVariable = "SBVZ_EMERGENCY_STOP_OBJECT_KEY";
    public const string DefaultObjectKey = "_control/sbvz-disabled";

    public string ObjectKey { get; set; } = DefaultObjectKey;
}
