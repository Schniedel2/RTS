using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTS.Network;

internal sealed class ComplexCommandJsonConverter : JsonConverter<NetworkMessage>
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> PlainOptions = new();
    private static JsonSerializerOptions Plain(JsonSerializerOptions source) => PlainOptions.GetValue(source, options =>
    {
        var copy = new JsonSerializerOptions(options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        for (int i = copy.Converters.Count - 1; i >= 0; i--)
            if (copy.Converters[i] is ComplexCommandJsonConverter) copy.Converters.RemoveAt(i);
        return copy;
    });

    public override NetworkMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Most traffic is state/snapshots. Avoid materializing a second JSON tree
        // for these unchanged messages; probe only as far as the discriminator.
        Utf8JsonReader probe = reader;
        if (probe.TokenType != JsonTokenType.StartObject) throw new JsonException("A network message must be an object.");
        NetworkMessageType? discriminator = null;
        while (probe.Read() && probe.TokenType != JsonTokenType.EndObject)
        {
            if (probe.TokenType != JsonTokenType.PropertyName) throw new JsonException("Invalid envelope.");
            bool isType = string.Equals(probe.GetString(), "type", StringComparison.OrdinalIgnoreCase);
            if (!probe.Read()) throw new JsonException("Missing envelope value.");
            if (isType)
            {
                if (probe.TokenType != JsonTokenType.Number || !probe.TryGetInt32(out int typeNumber))
                    throw new JsonException("A message requires a numeric type.");
                discriminator = (NetworkMessageType)typeNumber;
                break;
            }
            probe.Skip();
        }
        if (discriminator is null) throw new JsonException("Missing message type.");
        if (!ComplexCommandPayloads.Handles(discriminator.Value))
        {
            NetworkMessage plain = JsonSerializer.Deserialize<NetworkMessage>(ref reader, Plain(options))
                ?? throw new JsonException("Missing message.");
            if (ComplexCommandPayloads.Handles(plain.Type)) throw new JsonException("Ambiguous message type.");
            return plain;
        }
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("A network message must be an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in root.EnumerateObject())
            if (!fields.TryAdd(property.Name, property.Value)) throw new JsonException("Duplicate envelope field: " + property.Name);
        if (!fields.TryGetValue("type", out JsonElement type) || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out int number))
            throw new JsonException("A message requires a numeric type.");
        var kind = (NetworkMessageType)number;
        foreach (string name in fields.Keys)
            if (!name.Equals("type", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("senderId", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("serverTime", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("payload", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Unexpected envelope field: " + name);
        if (!fields.TryGetValue("senderId", out JsonElement sender) || sender.ValueKind != JsonValueKind.String || !sender.TryGetGuid(out Guid senderId) ||
            !fields.TryGetValue("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("A complex command requires senderId and a typed payload.");
        RejectDuplicates(payload);
        double time = 0;
        if (fields.TryGetValue("serverTime", out JsonElement timestamp) &&
            (timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetDouble(out time)))
            throw new JsonException("Invalid server time.");
        ComplexCommandPayload value = payload.Deserialize(ComplexCommandPayloads.PayloadType(kind), options) as ComplexCommandPayload
            ?? throw new JsonException("Missing payload.");
        return ComplexCommandPayloads.Create(senderId, value, time);
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!fields.Add(property.Name)) throw new JsonException("Duplicate payload field: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in element.EnumerateArray()) RejectDuplicates(child);
    }

    public override void Write(Utf8JsonWriter writer, NetworkMessage value, JsonSerializerOptions options)
    {
        if (!ComplexCommandPayloads.Handles(value.Type))
        {
            JsonSerializer.Serialize(writer, value, Plain(options));
            return;
        }
        ComplexCommandPayload payload = ComplexCommandPayloads.GetPayload(value);
        writer.WriteStartObject();
        writer.WriteNumber("type", (int)value.Type);
        writer.WriteString("senderId", value.SenderId);
        writer.WriteNumber("serverTime", value.ServerTime);
        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, payload, payload.GetType(), options);
        writer.WriteEndObject();
    }
}
