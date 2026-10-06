using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RTS;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AISelectionSettings(int SchemaVersion = 1, string? FixedProfileId = null,
    int? MatchSeed = null, int BalancedAssaultWeight = 40, int InfantryCompanyWeight = 25,
    int AntiArmorWeight = 25, int FastReconWeight = 10)
{
    public void Validate(string source)
    {
        int[] weights = [BalancedAssaultWeight, InfantryCompanyWeight, AntiArmorWeight, FastReconWeight];
        if (SchemaVersion != 1) throw new InvalidDataException($"{source}: $.schemaVersion must be 1.");
        if (FixedProfileId is not null && !Enum.GetValues<AIStrategyProfileType>().Any(t => AIProfileCatalog.Id(t) == FixedProfileId))
            throw new InvalidDataException($"{source}: $.fixedProfileId must name an existing profile.");
        string[] fields = ["balancedAssaultWeight", "infantryCompanyWeight", "antiArmorWeight", "fastReconWeight"];
        for (int i = 0; i < weights.Length; i++)
            if (weights[i] < 0 || weights[i] > 10000) throw new InvalidDataException($"{source}: $.{fields[i]} must be in 0..10000.");
        if (weights.Sum() == 0) throw new InvalidDataException($"{source}: $.weights must have a positive sum.");
    }
    public AIStrategyProfile Select(int matchSeed, Guid armyId, AIProfileCatalog profiles)
    {
        Validate("AI selection");
        int seed = AIStrategyProfile.EffectiveSeed(MatchSeed ?? matchSeed, armyId);
        AIStrategyProfileType type;
        if (FixedProfileId is not null)
            type = Enum.GetValues<AIStrategyProfileType>().Single(t => AIProfileCatalog.Id(t) == FixedProfileId);
        else
        {
            int roll = new Random(seed).Next(BalancedAssaultWeight + InfantryCompanyWeight + AntiArmorWeight + FastReconWeight);
            type = roll < BalancedAssaultWeight ? AIStrategyProfileType.BalancedAssault :
                roll < BalancedAssaultWeight + InfantryCompanyWeight ? AIStrategyProfileType.InfantryCompany :
                roll < BalancedAssaultWeight + InfantryCompanyWeight + AntiArmorWeight ? AIStrategyProfileType.AntiArmor : AIStrategyProfileType.FastRecon;
        }
        return profiles.Resolve(type, seed);
    }
}

// Machine settings never change strategic character, resources or visibility.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AIComputeSettings(int SchemaVersion = 1, int PlanningStepsPerUpdate = 2048,
    double PlanningMillisecondsPerUpdate = 2)
{
    public void Validate(string source)
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"{source}: $.schemaVersion must be 1.");
        if (PlanningStepsPerUpdate < 16 || PlanningStepsPerUpdate > 65536)
            throw new InvalidDataException($"{source}: $.planningStepsPerUpdate must be in 16..65536.");
        if (!double.IsFinite(PlanningMillisecondsPerUpdate) || PlanningMillisecondsPerUpdate < 0.1 || PlanningMillisecondsPerUpdate > 20)
            throw new InvalidDataException($"{source}: $.planningMillisecondsPerUpdate must be finite and in 0.1..20.");
    }
}

public static class AIRuntimeSettings
{
    private static readonly Lazy<AISelectionSettings> Selection = new(() => LoadSelection(Path.Combine(AppContext.BaseDirectory, "Config", "AI", "selection.json"), true));
    private static readonly Lazy<AIComputeSettings> Compute = new(() => LoadCompute(Path.Combine(AppContext.BaseDirectory, "Config", "AI", "compute.json"), true));
    public static AISelectionSettings Default => Selection.Value;
    public static AIComputeSettings LocalCompute => Compute.Value;
    public static AISelectionSettings LoadSelection(string file, bool optional = false)
    { var value = Load(file, optional, new AISelectionSettings()); value.Validate(file); return value; }
    public static AIComputeSettings LoadCompute(string file, bool optional = false)
    { var value = Load(file, optional, new AIComputeSettings()); value.Validate(file); return value; }
    private static T Load<T>(string file, bool optional, T fallback)
    {
        if (optional && !File.Exists(file)) return fallback;
        try
        {
            string json = File.ReadAllText(file);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{file}: $ must be an object.");
            var fields = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
                if (!fields.Add(field.Name)) throw new InvalidDataException($"{file}: $.{field.Name} occurs twice.");
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                ?? throw new InvalidDataException($"{file}: $ must not be null.");
        }
        catch (JsonException error) { throw new InvalidDataException($"{file}: {error.Path ?? "$"}: {error.Message}", error); }
    }
}
