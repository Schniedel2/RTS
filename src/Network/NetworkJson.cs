using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTS.Network;

/// <summary>
/// Network serialization must remain alive even if a simulation object briefly
/// produces a non-finite value. State consumers still reject those values.
/// </summary>
public static class NetworkJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
}
