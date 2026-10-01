using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

[Flags]
public enum AIUnitRole
{
    None = 0,
    Scout = 1 << 0,
    Attacker = 1 << 1,
    Defender = 1 << 2,
    AntiInfantry = 1 << 3,
    AntiVehicle = 1 << 4,
    AntiAir = 1 << 5,
    Support = 1 << 6,
    Harvester = 1 << 7,
    Builder = 1 << 8,
    Leader = 1 << 9,
    Healer = 1 << 10,
    Crew = 1 << 11
}

public enum AIMovementDomain
{
    Infantry,
    GroundVehicle,
    Air,
    Static
}

public sealed record AIUnitMetadata(
    AIUnitRole Roles,
    AIMovementDomain Movement,
    float AntiInfantry = 0.0f,
    float AntiVehicle = 0.0f,
    float AntiBuilding = 0.0f,
    float AntiAir = 0.0f,
    float Defense = 0.0f,
    float Mobility = 0.0f,
    float Scouting = 0.0f,
    int PreferredMaximumCount = 0);

public sealed record ProducerDefinition(string TypeId, float ProductionSeconds);

public sealed record BuildingMetadata(
    float MaxHitPoints,
    float ConstructionPoints,
    int PowerProduction = 0,
    int PowerConsumption = 0,
    float ResourceCapacity = 0.0f,
    int CrewCapacity = 0,
    int PowerProductionPerCrew = 0,
    int VisionRange = 0);

/// <summary>
/// Static data shared by production UI, host validation, economy and AI.
/// Producer lists deliberately support multiple buildings and future factions.
/// Runtime values and army-specific price modifiers do not belong here.
/// </summary>
public sealed record GameplayDefinition(
    PurchasableType Type,
    string TypeId,
    string DisplayName,
    int BasePrice,
    IReadOnlyList<PerkType> RequiredPerks,
    IReadOnlyList<ProducerDefinition> Producers,
    int IconColumn = 6,
    int IconRow = 1,
    AIUnitMetadata? AI = null,
    BuildingMetadata? Building = null,
    PerkType? GrantedPerk = null,
    IReadOnlyList<PerkType>? ProvidedPerks = null);

public static class GameplayCatalog
{
    private static readonly IReadOnlyList<PerkType> BaseRequired = [PerkType.BaseEstablished];
    private static readonly IReadOnlyList<PerkType> HelipadRequired =
        [PerkType.BaseEstablished, PerkType.AirTechnology];

    private static readonly List<GameplayDefinition> Definitions =
    [
        Building("gdi-base", "Base", 1500, [], new(2500, 2500, PowerConsumption: 40),
            providedPerks: [PerkType.BaseEstablished, PerkType.Home]),
        Building("gdi-barracks", "Barracks", 800, BaseRequired, new(2500, 2500)),
        Building("reaktor", "Reactor", 500, BaseRequired,
            new(500, 500, PowerProduction: 100, CrewCapacity: 4,
                PowerProductionPerCrew: 30)),
        Building("communicationstower", "Communications Tower", 500, BaseRequired,
            new(500, 500, PowerConsumption: 20, VisionRange: 50)),
        Building("helipad", "Helipad", 1500, HelipadRequired, new(1500, 1500, PowerConsumption: 15)),
        Building("silo", "Silo", 1500, BaseRequired, new(1500, 1500, PowerConsumption: 5, ResourceCapacity: 2500)),
        Building("tiberium-refinery", "Tiberium Refinery", 1500, BaseRequired, new(2500, 4500, PowerConsumption: 30, ResourceCapacity: 5000)),
        Building("vehicle-factory", "Vehicle Factory", 2000, BaseRequired, new(2500, 2500, PowerConsumption: 30)),
        Building("command-center", "Command Center", 0, BaseRequired, new(1000, 0)),
        Building("turret-minigun", "Gatling Turret", 1000, BaseRequired,
            new(500, 500, PowerConsumption: 20),
            [Producer("gdi-bulldozer", 0)],
            new(AIUnitRole.Defender | AIUnitRole.AntiInfantry | AIUnitRole.AntiAir,
                AIMovementDomain.Static, AntiInfantry: 0.9f, AntiVehicle: 0.2f,
                AntiAir: 0.85f, Defense: 0.8f, PreferredMaximumCount: 3)),
        Building("tiberium-source", "Tiberium Source", 0, [], new(300, 0),
            producers: Array.Empty<ProducerDefinition>()),

        Unit("gdi-bulldozer", "Bulldozer", 0, [Producer("gdi-base", 8)],
            new(AIUnitRole.Builder, AIMovementDomain.GroundVehicle, Defense: 0.4f, Mobility: 0.3f)),
        Unit("gunner", "Gunner", 100, [Producer("gdi-barracks", 4)],
            new(AIUnitRole.Scout | AIUnitRole.Attacker | AIUnitRole.Defender | AIUnitRole.AntiInfantry,
                AIMovementDomain.Infantry, AntiInfantry: 0.8f, Mobility: 0.5f)),
        Unit("rak-zero", "Rak Zero", 300, [Producer("gdi-barracks", 5)],
            new(AIUnitRole.Attacker | AIUnitRole.Defender | AIUnitRole.AntiVehicle,
                AIMovementDomain.Infantry, AntiVehicle: 0.9f, AntiAir: 0.5f, Mobility: 0.45f)),
        Unit("engineer", "Engineer", 300, [Producer("gdi-barracks", 5)],
            new(AIUnitRole.Support | AIUnitRole.Crew,
                AIMovementDomain.Infantry, Mobility: 0.45f)),
        Unit("medic", "Medic", 300, [Producer("gdi-barracks", 5)],
            new(AIUnitRole.Support | AIUnitRole.Healer,
                AIMovementDomain.Infantry, Mobility: 0.5f)),
        Unit("squad-leader", "Squad Leader", 400, [Producer("gdi-barracks", 6)],
            new(AIUnitRole.Attacker | AIUnitRole.Support | AIUnitRole.Leader,
                AIMovementDomain.Infantry, AntiInfantry: 0.4f, Mobility: 0.5f)),
        Unit("harvester", "Harvester", 500, [Producer("tiberium-refinery", 10)],
            new(AIUnitRole.Harvester, AIMovementDomain.GroundVehicle, Mobility: 0.3f)),
        Unit("tank", "Tank", 1000, [Producer("vehicle-factory", 10)],
            new(AIUnitRole.Attacker | AIUnitRole.Defender | AIUnitRole.AntiVehicle,
                AIMovementDomain.GroundVehicle, AntiInfantry: 0.35f, AntiVehicle: 1.0f,
                AntiBuilding: 0.7f, Defense: 0.9f, Mobility: 0.45f, PreferredMaximumCount: 8)),
        Unit("jeep", "Jeep", 400, [Producer("vehicle-factory", 7)],
            new(AIUnitRole.Scout | AIUnitRole.Attacker | AIUnitRole.AntiInfantry,
                AIMovementDomain.GroundVehicle, AntiInfantry: 0.65f, Defense: 0.25f, Mobility: 0.9f,
                Scouting: 0.9f, PreferredMaximumCount: 5)),
        Unit("helicopter", "Helicopter", 1200, [Producer("helipad", 10)],
            new(AIUnitRole.Scout | AIUnitRole.Attacker | AIUnitRole.AntiVehicle,
                AIMovementDomain.Air, AntiInfantry: 0.5f, AntiVehicle: 0.9f,
                AntiBuilding: 0.4f, Mobility: 1.0f, Scouting: 0.8f,
                PreferredMaximumCount: 4)),
        Unit("motorbike", "Motorbike", 400, [Producer("vehicle-factory", 4), Producer("gdi-barracks", 4)], new(AIUnitRole.Scout | AIUnitRole.Support, AIMovementDomain.GroundVehicle, AntiInfantry: 0.25f, Defense: 0.15f, Mobility: 1.0f, Scouting: 1.0f, PreferredMaximumCount: 5)),

        new(PurchasableType.Research, ResearchProjects.AirTechnologyId, "Air Technology", 1000,
            BaseRequired, [Producer("gdi-base", 15)], 4, 4,
            GrantedPerk: PerkType.AirTechnology)
    ];

    private static readonly IReadOnlyList<GameplayDefinition> PublicDefinitions = Definitions.AsReadOnly();
    static GameplayCatalog() => Validate();
    public static IReadOnlyList<GameplayDefinition> All => PublicDefinitions;

    /// <summary>Fails early for incomplete product/producer registrations and invalid purchase data.</summary>
    public static void Validate(IEnumerable<GameplayDefinition>? definitions = null)
    {
        GameplayDefinition[] products = (definitions ?? Definitions).ToArray();
        var keys = new HashSet<(PurchasableType, string)>();
        var ids = new HashSet<string>();
        foreach (GameplayDefinition product in products)
        {
            string id = Canonicalize(product.TypeId);
            if (!Enum.IsDefined(product.Type) || string.IsNullOrWhiteSpace(id) || id != product.TypeId ||
                !keys.Add((product.Type, id)) || !ids.Add(id))
                throw new InvalidOperationException($"Invalid or duplicate catalog product '{product.Type}:{product.TypeId}'.");
            if (product.BasePrice < 0 || product.RequiredPerks.Any(perk => !Enum.IsDefined(perk)))
                throw new InvalidOperationException($"Invalid purchase rules for '{id}'.");
            if (product.Type == PurchasableType.Unit && !UnitFactory.CanCreate(id) ||
                product.Type == PurchasableType.Building && !BuildingFactory.CanCreate(id))
                throw new InvalidOperationException($"No factory registered for '{product.Type}:{id}'.");
            if (product.Type == PurchasableType.Research &&
                (product.GrantedPerk is not PerkType granted || !Enum.IsDefined(granted)))
                throw new InvalidOperationException($"Research '{id}' must grant a valid perk.");
            if (product.ProvidedPerks is { Count: > 0 } provided &&
                (product.Type != PurchasableType.Building || provided.Any(perk => !Enum.IsDefined(perk)) ||
                    provided.Distinct().Count() != provided.Count))
                throw new InvalidOperationException($"Invalid building perk capabilities for '{id}'.");
            var producers = new HashSet<string>();
            foreach (ProducerDefinition producer in product.Producers)
            {
                string producerId = Canonicalize(producer.TypeId);
                if (!producers.Add(producerId) ||
                    !products.Any(candidate => candidate.Type != PurchasableType.Research && candidate.TypeId == producerId) ||
                    !float.IsFinite(producer.ProductionSeconds) || producer.ProductionSeconds < 0 ||
                    product.Type != PurchasableType.Building && producer.ProductionSeconds == 0 ||
                    product.Type == PurchasableType.Research && !BuildingFactory.CanCreate(producerId))
                    throw new InvalidOperationException($"Invalid producer '{producer.TypeId}' for '{id}'.");
            }
            if (product.Type == PurchasableType.Research && product.Producers.Count == 0)
                throw new InvalidOperationException($"Research '{id}' requires a producer.");
        }
        foreach (string id in UnitFactory.RegisteredTypeIds.Where(id => !UnitFactory.IsSandboxType(id)))
            if (!keys.Contains((PurchasableType.Unit, Canonicalize(id))))
                throw new InvalidOperationException($"Unit factory '{id}' is missing from the catalog.");
        foreach (string id in BuildingFactory.RegisteredTypeIds.Where(id => !BuildingFactory.IsSandboxType(id)))
            if (!keys.Contains((PurchasableType.Building, Canonicalize(id))))
                throw new InvalidOperationException($"Building factory '{id}' is missing from the catalog.");
    }

    public static GameplayDefinition? Find(PurchasableType type, string? typeId)
    {
        string canonical = Canonicalize(typeId);
        return Definitions.FirstOrDefault(definition => definition.Type == type &&
            string.Equals(definition.TypeId, canonical, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<GameplayDefinition> GetProducedBy(
        string producerTypeId, PurchasableType type = PurchasableType.Unit)
    {
        string producer = Canonicalize(producerTypeId);
        return Definitions.Where(definition => definition.Type == type &&
            definition.Producers.Any(candidate => string.Equals(
                candidate.TypeId, producer, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public static bool TryGetProductionDuration(string producerTypeId, PurchasableType type,
        string productTypeId, out float durationSeconds)
    {
        GameplayDefinition? definition = Find(type, productTypeId);
        ProducerDefinition? producer = definition?.Producers.FirstOrDefault(candidate =>
            string.Equals(candidate.TypeId, Canonicalize(producerTypeId),
                StringComparison.OrdinalIgnoreCase));
        durationSeconds = producer?.ProductionSeconds ?? 0.0f;
        return durationSeconds > 0.0f;
    }

    public static IReadOnlyList<UnitAction> CreateProductionActions(string producerTypeId) =>
        GetProducedBy(producerTypeId)
            .Select(definition => new UnitAction(UnitActionType.TrainUnit,
                definition.DisplayName, definition.IconColumn, definition.IconRow,
                definition.TypeId, RequiresTarget: false))
            .ToArray();

    public static IReadOnlyList<UnitAction> CreateResearchActions(string producerTypeId) =>
        GetProducedBy(producerTypeId, PurchasableType.Research)
            .Select(definition => new UnitAction(UnitActionType.Research,
                $"Research {definition.DisplayName}", definition.IconColumn, definition.IconRow,
                definition.TypeId, RequiresTarget: false))
            .ToArray();

    public static string Canonicalize(string? typeId) => typeId?.Trim().ToLowerInvariant() switch
    {
        "heli" => "helicopter",
        "soldier" => "gunner",
        string value => value,
        _ => string.Empty
    };

    public static bool HasAIRoles(string? typeId, AIUnitRole roles) =>
        Find(PurchasableType.Unit, typeId)?.AI is AIUnitMetadata ai &&
        (ai.Roles & roles) == roles;

    private static GameplayDefinition Building(string id, string name, int price,
        IReadOnlyList<PerkType> perks, BuildingMetadata metadata,
        IReadOnlyList<ProducerDefinition>? producers = null, AIUnitMetadata? ai = null,
        IReadOnlyList<PerkType>? providedPerks = null) =>
        new(PurchasableType.Building, id, name, price, perks,
            producers ?? [Producer("gdi-bulldozer", 0)], AI: ai,
            Building: metadata, ProvidedPerks: providedPerks);

    private static GameplayDefinition Unit(string id, string name, int price,
        IReadOnlyList<ProducerDefinition> producers, AIUnitMetadata ai) =>
        new(PurchasableType.Unit, id, name, price, [], producers, AI: ai);

    private static ProducerDefinition Producer(string id, float seconds) => new(id, seconds);
}
