using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RTS;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ServerAISlot([property: JsonRequired] string Name,
    string ProfileId = "balanced-assault", int Team = 0);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DedicatedServerConfig([property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string MapDirectory, int Port = 27000, string SessionName = "RTS Server",
    int MaximumPlayers = 4, int RequiredPlayers = 1, string StartMode = "when-ready", bool AllowLateJoin = true,
    int MatchSeed = 1234, ServerAISlot[]? AIPlayers = null, string? ControlFile = null,
    string? StatusPath = null, string? DiagnosticsPath = null, bool AutoSelectPort = false)
{
    public static DedicatedServerConfig Load(string path)
    {
        path = Path.GetFullPath(path);
        try
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Server config must be an object.");
            ValidateKeys(document.RootElement);
            var value = JsonSerializer.Deserialize<DedicatedServerConfig>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                ?? throw new InvalidDataException("Server config is null.");
            value.Validate();
            string Resolve(string file) => Path.GetFullPath(file, Path.GetDirectoryName(path)!);
            var resolved = value with { MapDirectory = Resolve(value.MapDirectory), ControlFile = value.ControlFile is null ? null : Resolve(value.ControlFile),
                StatusPath = value.StatusPath is null ? null : Resolve(value.StatusPath), DiagnosticsPath = value.DiagnosticsPath is null ? null : Resolve(value.DiagnosticsPath) };
            resolved.Validate();
            if (new[] { resolved.ControlFile, resolved.StatusPath, resolved.DiagnosticsPath }.Contains(path, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Output/control paths cannot replace the config file.");
            return resolved;
        }
        catch (JsonException error) { throw new InvalidDataException($"{path}: {error.Path}: {error.Message}", error); }
    }
    private static void ValidateKeys(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate config field.");
            foreach (var property in node.EnumerateObject()) ValidateKeys(property.Value);
        }
        if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) ValidateKeys(child);
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(MapDirectory)) throw new InvalidDataException("schemaVersion must be 1 and mapDirectory is required.");
        if (Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(SessionName) || SessionName.Length > 32) throw new InvalidDataException("Invalid port/sessionName.");
        if (MaximumPlayers is < 0 or > 32 || RequiredPlayers < 0 || RequiredPlayers > MaximumPlayers) throw new InvalidDataException("Invalid player slot count.");
        if (StartMode is not ("manual" or "when-ready")) throw new InvalidDataException("startMode must be manual or when-ready.");
        if (new[] { ControlFile, StatusPath, DiagnosticsPath }.Any(p => p is not null && string.IsNullOrWhiteSpace(p))) throw new InvalidDataException("Optional paths cannot be empty.");
        var slots = AIPlayers ?? [];
        if (StartMode == "when-ready" && RequiredPlayers == 0 && slots.Length == 0) throw new InvalidDataException("Automatic start needs at least one participant.");
        if (slots.Length > 32 || MaximumPlayers + slots.Length == 0 || slots.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 32 || s.Team < 0 ||
            !Enum.GetValues<AIStrategyProfileType>().Any(t => AIProfileCatalog.Id(t) == s.ProfileId))) throw new InvalidDataException("Invalid AI slots.");
        if (slots.Select(s => s.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != slots.Length) throw new InvalidDataException("Duplicate AI slot name.");
        var outputs = new[] { ControlFile, StatusPath, DiagnosticsPath }.Where(p => p is not null).Select(p => Path.GetFullPath(p!)).ToArray();
        if (outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != outputs.Length) throw new InvalidDataException("Control/status/diagnostics paths must differ.");
    }
}
