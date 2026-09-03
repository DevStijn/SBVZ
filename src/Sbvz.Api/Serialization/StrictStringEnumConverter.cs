using System.Text.Json.Serialization;

namespace Sbvz.Api.Serialization;

public sealed class StrictStringEnumConverter<TEnum>
    : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum
{
    public StrictStringEnumConverter()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}
