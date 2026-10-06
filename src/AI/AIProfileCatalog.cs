using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTS;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AIProfileConfig(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string ProfileId,
    [property: JsonRequired] AIStrategyProfileType Type,
    [property: JsonRequired] string DisplayName,
    [property: JsonRequired] int RequiredGunners,
    [property: JsonRequired] int RequiredRakZero,
    [property: JsonRequired] int RequiredTanks,
    [property: JsonRequired] float RetreatHealthFraction,
    [property: JsonRequired] float AttackReadinessSeconds,
    [property: JsonRequired] float DefenseRadiusInCells,
    [property: JsonRequired] float AssaultStallTimeoutSeconds,
    float DecisionIntervalSeconds = 1,
    int ResourceReserve = 800,
    float ScoutReconsiderSeconds = 8);

/// <summary>Immutable profile definitions loaded once, never during controller updates.</summary>
public sealed class AIProfileCatalog
{
    private static readonly Lazy<AIProfileCatalog> _default = new(() => LoadDirectory(
        Path.Combine(AppContext.BaseDirectory, "Config", "AI", "Profiles"), optional: true));
    public static AIProfileCatalog Default => _default.Value;
    public IReadOnlyDictionary<AIStrategyProfileType, AIProfileConfig> Profiles { get; }
    public string SourceDirectory { get; }
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<AIStrategyProfileType>(allowIntegerValues: false) }
    };

    private AIProfileCatalog(Dictionary<AIStrategyProfileType, AIProfileConfig> profiles, string directory)
    { Profiles = new ReadOnlyDictionary<AIStrategyProfileType, AIProfileConfig>(profiles); SourceDirectory = directory; }

    public static string Id(AIStrategyProfileType type) => type switch
    {
        AIStrategyProfileType.BalancedAssault => "balanced-assault",
        AIStrategyProfileType.InfantryCompany => "infantry-company",
        AIStrategyProfileType.AntiArmor => "anti-armor",
        AIStrategyProfileType.FastRecon => "fast-recon",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static AIProfileConfig BuiltIn(AIStrategyProfileType type) => type switch
    {
        AIStrategyProfileType.BalancedAssault => new(1, Id(type), type, "Balanced Assault", 3, 1, 1, 0.45f, 5, 36, 60),
        AIStrategyProfileType.InfantryCompany => new(1, Id(type), type, "Infantry Company", 4, 0, 1, 0.5f, 7, 40, 70),
        AIStrategyProfileType.AntiArmor => new(1, Id(type), type, "Anti-Armor", 2, 2, 2, 0.42f, 6, 34, 65),
        AIStrategyProfileType.FastRecon => new(1, Id(type), type, "Fast Recon", 2, 1, 1, 0.36f, 2, 30, 45),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public AIStrategyProfile Resolve(AIStrategyProfileType type, int seed)
    {
        AIProfileConfig profile = Profiles[type];
        return new(type, profile.DisplayName, profile.RequiredGunners, profile.RequiredRakZero, profile.RequiredTanks,
            profile.RetreatHealthFraction, profile.AttackReadinessSeconds, profile.DefenseRadiusInCells,
            profile.AssaultStallTimeoutSeconds, seed, profile.DecisionIntervalSeconds, profile.ResourceReserve, profile.ScoutReconsiderSeconds);
    }

    public static AIProfileConfig Parse(string json, string source)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{source}: $ must be an object.");
            HashSet<string> fields = new(StringComparer.Ordinal);
            foreach (JsonProperty field in document.RootElement.EnumerateObject())
                if (!fields.Add(field.Name)) throw new InvalidDataException($"{source}: $.{field.Name} occurs more than once.");
            AIProfileConfig profile = JsonSerializer.Deserialize<AIProfileConfig>(json, Options)
                ?? throw new InvalidDataException($"{source}: $ must not be null.");
            void Require(bool condition, string field, string requirement)
            { if (!condition) throw new InvalidDataException($"{source}: $.{field} {requirement}."); }
            void Number(float value, string field, float min, float max) =>
                Require(float.IsFinite(value) && value >= min && value <= max, field, $"must be finite and in {min}..{max}");
            Require(profile.SchemaVersion == 1, "schemaVersion", "must be 1");
            Require(Enum.IsDefined(profile.Type), "type", "must be a known strategy type");
            Require(profile.ProfileId == Id(profile.Type), "profileId", $"must be '{Id(profile.Type)}' for this type");
            Require(!string.IsNullOrWhiteSpace(profile.DisplayName) && profile.DisplayName.Length <= 80,
                "displayName", "must contain 1..80 nonblank characters");
            Number(profile.RequiredGunners, "requiredGunners", 0, 8);
            Number(profile.RequiredRakZero, "requiredRakZero", 0, 8);
            Require(profile.RequiredGunners + profile.RequiredRakZero <= 8, "requiredGunners/requiredRakZero", "combined count must not exceed 8");
            Number(profile.RequiredTanks, "requiredTanks", 0, 20);
            Number(profile.RetreatHealthFraction, "retreatHealthFraction", 0, 1);
            Number(profile.AttackReadinessSeconds, "attackReadinessSeconds", 0, 600);
            Number(profile.DefenseRadiusInCells, "defenseRadiusInCells", 1, 256);
            Number(profile.AssaultStallTimeoutSeconds, "assaultStallTimeoutSeconds", 1, 600);
            Number(profile.DecisionIntervalSeconds, "decisionIntervalSeconds", 0.25f, 10);
            Number(profile.ResourceReserve, "resourceReserve", 0, 100000);
            Number(profile.ScoutReconsiderSeconds, "scoutReconsiderSeconds", 1, 120);
            return profile;
        }
        catch (JsonException error)
        { throw new InvalidDataException($"{source}: {error.Path ?? "$"}: {error.Message}", error); }
    }

    public static AIProfileCatalog LoadDirectory(string directory, bool optional = false)
    {
        string fullPath = Path.GetFullPath(directory);
        Dictionary<AIStrategyProfileType, AIProfileConfig> profiles = Enum.GetValues<AIStrategyProfileType>()
            .ToDictionary(type => type, BuiltIn);
        if (!Directory.Exists(fullPath))
        {
            if (optional) return new(profiles, fullPath);
            throw new DirectoryNotFoundException($"AI profile directory not found: {fullPath}");
        }
        HashSet<AIStrategyProfileType> loaded = [];
        foreach (string file in Directory.EnumerateFiles(fullPath, "*.json").OrderBy(file => file, StringComparer.Ordinal))
        {
            AIProfileConfig profile;
            try { profile = Parse(File.ReadAllText(file), file); }
            catch (IOException error)
            { throw new IOException($"Cannot read AI profile '{file}': {error.Message}", error); }
            if (!loaded.Add(profile.Type)) throw new InvalidDataException($"{file}: $.type duplicates '{profile.Type}'.");
            profiles[profile.Type] = profile;
        }
        // Required directories must contain all profiles; optional defaults may omit individual files.
        if (!optional && loaded.Count != profiles.Count)
            throw new InvalidDataException($"{fullPath}: missing profiles: {string.Join(", ", profiles.Keys.Where(type => !loaded.Contains(type)).Select(Id))}.");
        return new(profiles, fullPath);
    }
}
