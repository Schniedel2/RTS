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
    Ready
}

/// <summary>Builds one vehicle factory and maintains the profile's initial tank complement.</summary>
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
    public string LastDecision { get; private set; } = "Waiting to establish armored production.";

    public void Update(GameTime gameTime)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        if (State == AIArmoredSupportState.FactoryRequested)
            _requestElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        VehicleFactory? factory = _factoryId is Guid factoryId
            ? world.Units.FindById(factoryId) as VehicleFactory
            : null;
        factory ??= world.Units.Units.OfType<VehicleFactory>()
            .FirstOrDefault(building => building.ArmyId == actor.ArmyId && !building.IsDying);
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

        float vehicleBias = profile.Type == AIStrategyProfileType.AntiArmor ? 0.45f : 0.15f;
        float infantryBias = profile.Type == AIStrategyProfileType.FastRecon ? 0.35f : 0.1f;
        GameplayDefinition? selected = AIUnitSelector.SelectBest(factory.GameplayTypeId,
            _threat.CreateGroundCombatNeed(infantryBias, vehicleBias), CountOwnedCatalogUnits());
        if (selected is null)
        {
            State = AIArmoredSupportState.WaitingForResources;
            LastDecision = "Vehicle factory has no catalog unit suitable for armored support.";
            return;
        }

        int vehicles = world.Units.Units.Count(unit => unit.ArmyId == actor.ArmyId &&
            !unit.IsDying && IsArmoredSupport(unit));
        int queued = factory.ProductionQueue.Orders.Count(order =>
            GameplayCatalog.Find(PurchasableType.Unit, order.UnitTypeId)?.AI is AIUnitMetadata ai &&
            IsArmoredSupport(ai));
        if (vehicles + queued >= profile.RequiredTanks)
        {
            State = vehicles >= profile.RequiredTanks
                ? AIArmoredSupportState.Ready
                : AIArmoredSupportState.TrainingTanks;
            LastDecision = vehicles >= profile.RequiredTanks
                ? $"Armored support ready ({vehicles}/{profile.RequiredTanks} vehicles)."
                : $"Training armored support ({vehicles}/{profile.RequiredTanks} ready, {queued} queued).";
            return;
        }

        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Unit, selected.TypeId, actor.ArmyId, factory.UnitId));
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (!quote.IsAvailable || army is null || army.Resources < quote.FinalPrice + ResourceReserve)
        {
            State = AIArmoredSupportState.WaitingForResources;
            LastDecision = $"Holding {ResourceReserve} resources in reserve before ordering " +
                $"{selected.DisplayName} {vehicles + 1}/{profile.RequiredTanks}.";
            return;
        }

        _ = _commands.TrainUnitAsync(factory.UnitId, selected.TypeId);
        State = AIArmoredSupportState.TrainingTanks;
        LastDecision = $"Ordered {selected.DisplayName} {vehicles + queued + 1}/{profile.RequiredTanks}.";
    }

    private System.Collections.Generic.IReadOnlyDictionary<string, int> CountOwnedCatalogUnits() =>
        world.Units.Units
            .Where(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying &&
                !string.IsNullOrWhiteSpace(unit.GameplayTypeId))
            .GroupBy(unit => unit.GameplayTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    private static bool IsArmoredSupport(Unit unit) =>
        GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai &&
        IsArmoredSupport(ai);

    private static bool IsArmoredSupport(AIUnitMetadata ai) =>
        ai.Movement == AIMovementDomain.GroundVehicle &&
        ai.Roles.HasFlag(AIUnitRole.Attacker);

    private void BuildFactory()
    {
        if (State == AIArmoredSupportState.FactoryRequested && _requestElapsed < RequestTimeoutSeconds)
        {
            LastDecision = "Waiting for host confirmation of the vehicle factory.";
            return;
        }

        const string typeId = "vehicle-factory";
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, typeId, actor.ArmyId));
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (!quote.IsAvailable || army is null || army.Resources < quote.FinalPrice + ResourceReserve)
        {
            State = AIArmoredSupportState.WaitingForResources;
            LastDecision = $"Waiting for vehicle factory cost plus {ResourceReserve} reserve resources.";
            return;
        }

        GDIBulldozer? bulldozer = ArmyGoalController.FindBulldozer(world, actor.ArmyId);
        if (bulldozer is null)
        {
            State = AIArmoredSupportState.FindingFactorySite;
            LastDecision = "Armored production requires an available bulldozer.";
            return;
        }

        var preview = new VehicleFactory(Vector3.Zero, Guid.NewGuid(), "vehicle-factory-1", quote.FinalPrice);
        ArmyGoalController.PreparePreview(preview, actor);
        Building? home = world.Units.Units.OfType<GDIBase>()
            .FirstOrDefault(building => building.ArmyId == actor.ArmyId && !building.IsDying);
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
