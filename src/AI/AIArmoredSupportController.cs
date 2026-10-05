using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;

namespace RTS;

public enum AIArmoredSupportState
{
    WaitingForResources,
    FindingFactorySite,
    FactoryRequested,
    ConstructingFactory,
    TrainingTanks,
    TrainingAirDefense,
    Ready
}

/// <summary>Maintains ground combat vehicles and a separate mobile air-defense complement.</summary>
public sealed class AIArmoredSupportController(
    GameWorld world,
    Player actor,
    NetworkHandler network,
    AIStrategyProfile profile,
    AIThreatAssessment? threatAssessment = null)
{
    public const int ResourceReserve = 800;
    private const float ThinkIntervalSeconds = 1.0f;
    private const float RequestTimeoutSeconds = 3.0f;
    private readonly PlayerCommandService _commands = new(network, actor.Id);
    private readonly AIThreatAssessment _threat = threatAssessment ?? new(actor.ArmyId);
    private float _thinkElapsed;
    private float _requestElapsed;
    private Guid? _factoryId;

    public AIArmoredSupportState State { get; private set; } = AIArmoredSupportState.WaitingForResources;
    public bool IsReady => State == AIArmoredSupportState.Ready;
    public bool HasOperationalFactory => AIStrategicCatalog.FindBuilding(world, actor.ArmyId,
        AIStrategicBuildingNeed.ArmoredProduction, _factoryId) is { IsCompleted: true };
    public string LastDecision { get; private set; } = "Waiting to establish armored production.";

    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.ArmoredSupport");
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        if (State == AIArmoredSupportState.FactoryRequested)
            _requestElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        Building? factory = AIStrategicCatalog.FindBuilding(world, actor.ArmyId,
            AIStrategicBuildingNeed.ArmoredProduction, _factoryId);
        if (factory is null)
        {
            BuildFactory();
            return;
        }

        _factoryId = factory.UnitId;
        if (!factory.IsCompleted)
        {
            State = AIArmoredSupportState.ConstructingFactory;
            LastDecision = $"Constructing vehicle factory ({factory.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        int airDefenses = CountSupport(IsMobileAirDefense, queued: false);
        int queuedAirDefenses = CountSupport(IsMobileAirDefense, queued: true);
        int desiredAirDefenses = DesiredMobileAirDefenseCount(_threat.Current.AntiAirNeed);
        // Existing ground forces must not hide a new need for air defense.
        // Against observed aircraft this order has priority over replenishing tanks.
        if (_threat.Current.AntiAirNeed >= AIDefensePlanner.AirThreatThreshold &&
            airDefenses + queuedAirDefenses < desiredAirDefenses && TryOrderAirDefense())
            return;

        float vehicleBias = profile.Type == AIStrategyProfileType.AntiArmor ? 0.45f : 0.15f;
        float infantryBias = profile.Type == AIStrategyProfileType.FastRecon ? 0.35f : 0.1f;
        GameplayDefinition? selected = AIStrategicCatalog.SelectUnit(world, actor.ArmyId,
            _threat.CreateGroundCombatNeed(infantryBias, vehicleBias), requireProducer: true,
            currentCounts: CountOwnedCatalogUnits(),
            candidateFilter: definition => definition.AI is AIUnitMetadata ai && IsArmoredSupport(ai));
        if (selected is null)
        {
            State = AIArmoredSupportState.WaitingForResources;
            LastDecision = "Vehicle factory has no catalog unit suitable for armored support.";
            return;
        }

        Building? producer = AIStrategicCatalog.FindAvailableProducer(world, actor.ArmyId, selected);
        if (producer is null) return;
        int vehicles = CountSupport(IsArmoredSupport, queued: false);
        int queued = CountSupport(IsArmoredSupport, queued: true);
        if (vehicles + queued >= profile.RequiredTanks)
        {
            if (airDefenses + queuedAirDefenses < desiredAirDefenses && TryOrderAirDefense())
                return;
            if (airDefenses < desiredAirDefenses)
            {
                State = queuedAirDefenses > 0 ? AIArmoredSupportState.TrainingAirDefense : AIArmoredSupportState.WaitingForResources;
                LastDecision = $"Mobile air defense ({airDefenses}/{desiredAirDefenses} ready, {queuedAirDefenses} queued).";
                return;
            }
            State = vehicles >= profile.RequiredTanks
                ? AIArmoredSupportState.Ready
                : AIArmoredSupportState.TrainingTanks;
            LastDecision = vehicles >= profile.RequiredTanks
                ? $"Armored support ready ({vehicles}/{profile.RequiredTanks} vehicles)."
                : $"Training armored support ({vehicles}/{profile.RequiredTanks} ready, {queued} queued).";
            return;
        }

        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Unit, selected.TypeId, actor.ArmyId, producer.UnitId));
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (!quote.IsAvailable || army is null || army.Resources < quote.FinalPrice + ResourceReserve)
        {
            State = AIArmoredSupportState.WaitingForResources;
            LastDecision = $"Holding {ResourceReserve} resources in reserve before ordering " +
                $"{selected.DisplayName} {vehicles + 1}/{profile.RequiredTanks}.";
            return;
        }

        _ = _commands.TrainUnitAsync(producer.UnitId, selected.TypeId);
        State = AIArmoredSupportState.TrainingTanks;
        LastDecision = $"Ordered {selected.DisplayName} {vehicles + queued + 1}/{profile.RequiredTanks}.";
    }

    private System.Collections.Generic.IReadOnlyDictionary<string, int> CountOwnedCatalogUnits() =>
        world.Units.GetArmyUnits(actor.ArmyId)
            .Where(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying &&
                !string.IsNullOrWhiteSpace(unit.GameplayTypeId))
            .GroupBy(unit => unit.GameplayTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static bool IsArmoredSupport(AIUnitMetadata ai) =>
        ai.Movement == AIMovementDomain.GroundVehicle &&
        ai.Roles.HasFlag(AIUnitRole.Attacker) &&
        (ai.AntiInfantry > 0 || ai.AntiVehicle > 0 || ai.AntiBuilding > 0);

    private static bool IsMobileAirDefense(AIUnitMetadata ai) =>
        ai.Movement == AIMovementDomain.GroundVehicle && ai.AntiAir > 0 &&
        ai.Roles.HasFlag(AIUnitRole.Defender | AIUnitRole.AntiAir);

    public static int DesiredMobileAirDefenseCount(float airThreat) =>
        Math.Clamp((int)MathF.Ceiling(airThreat / AIDefensePlanner.AirThreatPerDefense), 1, 3);

    private int CountSupport(Func<AIUnitMetadata, bool> matches, bool queued)
    {
        var units = world.Units.GetArmyUnits(actor.ArmyId).Where(unit => !unit.IsDying);
        return queued
            ? units.OfType<Building>().SelectMany(building => building.ProductionQueue.Orders)
                .Count(order => GameplayCatalog.Find(PurchasableType.Unit, order.UnitTypeId)?.AI is AIUnitMetadata ai && matches(ai))
            : units.Count(unit => GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai && matches(ai));
    }

    private bool TryOrderAirDefense()
    {
        GameplayDefinition? product = AIStrategicCatalog.SelectUnit(world, actor.ArmyId,
            new(AIUnitRole.Defender | AIUnitRole.AntiAir, AIMovementDomain.GroundVehicle,
                AntiAir: 1.0f, Defense: 0.5f), requireProducer: true,
            currentCounts: CountOwnedCatalogUnits());
        Building? producer = product is null ? null : AIStrategicCatalog.FindAvailableProducer(world, actor.ArmyId, product);
        if (product is null || producer is null) return false;
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new(
            PurchasableType.Unit, product.TypeId, actor.ArmyId, producer.UnitId));
        if (!quote.IsAvailable || Globals.Game.Armies.Find(actor.ArmyId) is not Army army ||
            army.Resources < quote.FinalPrice + ResourceReserve) return false;
        _ = _commands.TrainUnitAsync(producer.UnitId, product.TypeId);
        State = AIArmoredSupportState.TrainingAirDefense;
        LastDecision = $"Ordered mobile air defense: {product.DisplayName}.";
        return true;
    }

    private void BuildFactory()
    {
        if (State == AIArmoredSupportState.FactoryRequested && _requestElapsed < RequestTimeoutSeconds)
        {
            LastDecision = "Waiting for host confirmation of the vehicle factory.";
            return;
        }

        GameplayDefinition? definition = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId,
            AIStrategicBuildingNeed.ArmoredProduction, requireAvailable: true);
        if (definition is null)
        {
            LastDecision = "No available armored-production offer.";
            return;
        }
        string typeId = definition.TypeId;
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, typeId, actor.ArmyId));
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (!quote.IsAvailable || army is null || army.Resources < quote.FinalPrice + ResourceReserve)
        {
            State = AIArmoredSupportState.WaitingForResources;
            LastDecision = $"Waiting for vehicle factory cost plus {ResourceReserve} reserve resources.";
            return;
        }

        MobileUnit? bulldozer = AIStrategicCatalog.FindBuilder(world, actor.ArmyId, typeId);
        if (bulldozer is null)
        {
            State = AIArmoredSupportState.FindingFactorySite;
            LastDecision = "Armored production requires an available bulldozer.";
            return;
        }

        Building? preview = BuildingFactory.SpawnBuilding(typeId, Vector3.Zero, 0, Guid.NewGuid(), actor.Id, quote.FinalPrice);
        if (preview is null) return;
        ArmyGoalController.PreparePreview(preview, actor);
        Building? home = AIStrategicCatalog.FindBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Base);
        Vector3 origin = home?.Position ?? bulldozer.Position;
        if (!ArmyGoalController.TryFindBuildingSite(world, preview, origin, 8, 22, out Vector3 position))
        {
            State = AIArmoredSupportState.FindingFactorySite;
            LastDecision = "No valid vehicle factory site found near the base; retrying.";
            return;
        }

        _factoryId = Guid.NewGuid();
        _ = _commands.BuildAndConstructAsync(typeId, position, 0.0f,
            [bulldozer.UnitId], _factoryId.Value);
        _requestElapsed = 0.0f;
        State = AIArmoredSupportState.FactoryRequested;
        LastDecision = $"Requested vehicle factory at ({position.X:0.0}, {position.Z:0.0}).";
    }
}
