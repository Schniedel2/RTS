using System;
using Microsoft.Xna.Framework;
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
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new GridPointJsonConverter() }
    };
}

// MonoGame Point exposes fields, which System.Text.Json omits by default. Keep
// this explicit rather than enabling every public field in all network types.
internal sealed class GridPointJsonConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement value = document.RootElement;
        if (!(value.TryGetProperty("x", out JsonElement x) || value.TryGetProperty("X", out x)) ||
            !(value.TryGetProperty("y", out JsonElement y) || value.TryGetProperty("Y", out y)))
            throw new JsonException("A route cell requires integer x and y coordinates.");
        return new Point(x.GetInt32(), y.GetInt32());
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}
