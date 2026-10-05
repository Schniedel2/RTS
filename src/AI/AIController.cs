using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum AIGoalState
{
    WaitingForMatch,
    FindingBulldozer,
    FindingBaseSite,
    BaseBuildRequested,
    ConstructingBase,
    FindingReactorSite,
    ReactorBuildRequested,
    ConstructingReactor,
    FindingRefinerySite,
    RefineryBuildRequested,
    ConstructingRefinery,
    WaitingForHarvester,
    EconomyOnline,
    FindingBarracksSite,
    BarracksBuildRequested,
    ConstructingBarracks,
    TrainingSoldiers,
    BaseDefenseReady,
    Scouting
}

public enum AIArmyGoal
{
    None,
    BuildReactor,
    BuildRefinery,
    EstablishEconomy
}

/// <summary>
/// Deliberately small host-side AI plan: construct one reactor followed by one
/// Tiberium refinery through the same requests a human player sends.
/// </summary>
public sealed class ArmyGoalController
{
    // Keep AI bases traversable even when a model has no authored pivot:clearance.
    // Three empty grid cells match the Harvester's width and keep its routes usable.
    public const int MinimumBuildingSpacingCells = 3;
    private const float ThinkIntervalSeconds = 1.0f;
    private const float BuildRequestTimeoutSeconds = 3.0f;
    private float _thinkElapsed;
    private float _requestElapsed;
    private Guid? _baseId;
    private Guid? _reactorId;
    private Guid? _refineryId;
    private Guid? _barracksId;
    private bool _harvestOrderIssued;
    private bool _rallyPointIssued;

    public AIGoalState Goal { get; private set; } = AIGoalState.WaitingForMatch;
    public AIArmyGoal ActiveGoal { get; private set; }
    public string LastDecision { get; private set; } = "Waiting for game-start.";

    public void Start(AIArmyGoal goal)
    {
        ActiveGoal = goal;
        Goal = AIGoalState.FindingBulldozer;
        LastDecision = $"Started {goal}; looking for an army bulldozer.";
        _thinkElapsed = ThinkIntervalSeconds;
        _requestElapsed = 0.0f;
        _baseId = null;
        _reactorId = null;
        _refineryId = null;
        _barracksId = null;
        _harvestOrderIssued = false;
        _rallyPointIssued = false;
    }

    public void Stop()
    {
        ActiveGoal = AIArmyGoal.None;
        Goal = AIGoalState.WaitingForMatch;
        LastDecision = "Army goal stopped.";
        _requestElapsed = 0.0f;
    }

    public void Update(GameTime gameTime, Player actor, GameWorld world, NetworkHandler network,
        Action<AIPlayerStatus>? setStatus = null)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.ArmyGoals");
        if (!network.IsHost || Goal == AIGoalState.WaitingForMatch)
            return;

        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += seconds;
        if (Goal is AIGoalState.BaseBuildRequested or AIGoalState.ReactorBuildRequested or
            AIGoalState.RefineryBuildRequested or
            AIGoalState.BarracksBuildRequested)
            _requestElapsed += seconds;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;
        var commands = new PlayerCommandService(network, actor.Id);

        if (ActiveGoal == AIArmyGoal.None)
        {
            if (Goal != AIGoalState.BaseDefenseReady ||
                !NeedsCoreMaintenance(world, actor.ArmyId))
                return;

            ActiveGoal = AIArmyGoal.EstablishEconomy;
            Goal = AIGoalState.FindingBulldozer;
            _requestElapsed = 0.0f;
            LastDecision = "Core infrastructure or base defense was lost; restarting the maintenance plan.";
        }

        Building? homeBase = AIStrategicCatalog.FindBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Base, _baseId);
        if (homeBase is null)
        {
            BuildBase(actor, world, commands, setStatus);
            return;
        }

        _baseId = homeBase.UnitId;
        if (!homeBase.IsCompleted)
        {
            EnsureConstruction(world, actor, commands, homeBase);
            Goal = AIGoalState.ConstructingBase;
            setStatus?.Invoke(AIPlayerStatus.Building);
            LastDecision = $"Constructing base ({homeBase.ConstructionPercentage * 100.0f:0}%).";
            return;
        }
        if (Globals.Game.Armies.Find(actor.ArmyId)?.Perks.Has(PerkType.BaseEstablished) != true)
        {
            Goal = AIGoalState.ConstructingBase;
            LastDecision = "Base completed; waiting for its construction network to become operational.";
            return;
        }

        MobileUnit? availableBulldozer = FindBuilder(world, actor.ArmyId);
        if (availableBulldozer is null)
        {
            GameplayDefinition? builderType = AIStrategicCatalog.SelectUnit(world, actor.ArmyId,
                new AIProductionNeed(AIUnitRole.Builder), requireProducer: true);
            Building? builderProducer = builderType is null ? null : AIStrategicCatalog.FindAvailableProducer(world, actor.ArmyId, builderType);
            if (builderType is null || builderProducer is null) { LastDecision = "No available builder offer."; return; }
            bool queued = builderProducer.ProductionQueue.Orders.Any(order => string.Equals(
                GameplayCatalog.Canonicalize(order.UnitTypeId), builderType.TypeId,
                StringComparison.OrdinalIgnoreCase));
            PurchaseQuote builderQuote = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, builderType.TypeId,
                actor.ArmyId, builderProducer.UnitId));
            if (!queued && !AIResourcePlanner.CanPropose(world, actor.ArmyId, builderQuote, AIOrderPriority.Survival))
            { LastDecision = $"Waiting for {builderQuote.FinalPrice} resources for a builder."; return; }
            if (!queued)
                _ = commands.TrainUnitAsync(builderProducer.UnitId, builderType.TypeId);
            Goal = AIGoalState.FindingBulldozer;
            setStatus?.Invoke(AIPlayerStatus.Building);
            LastDecision = queued
                ? "Waiting for a replacement bulldozer."
                : "Ordered a replacement bulldozer from the base.";
            return;
        }

        Building? reactor = AIStrategicCatalog.FindBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Power, _reactorId);
        if (ActiveGoal is AIArmyGoal.BuildReactor or AIArmyGoal.EstablishEconomy && reactor is null)
        {
            BuildReactor(actor, world, commands, setStatus);
            return;
        }

        if (reactor is not null)
        {
            _reactorId = reactor.UnitId;
            if (!reactor.IsCompleted && ActiveGoal is AIArmyGoal.BuildReactor or AIArmyGoal.EstablishEconomy)
            {
                EnsureConstruction(world, actor, commands, reactor);
                Goal = AIGoalState.ConstructingReactor;
                setStatus?.Invoke(AIPlayerStatus.Building);
                LastDecision = $"Constructing reactor ({reactor.ConstructionPercentage * 100.0f:0}%).";
                return;
            }
        }

        if (ActiveGoal == AIArmyGoal.BuildReactor)
        {
            Complete("Reactor goal completed.", setStatus);
            return;
        }

        Building? refinery = AIStrategicCatalog.FindBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Economy, _refineryId);
        if (refinery is null)
        {
            Vector3 origin = reactor?.Position ?? FindBuilder(world, actor.ArmyId)?.Position ?? Vector3.Zero;
            BuildRefinery(actor, world, commands, origin, setStatus);
            return;
        }

        _refineryId = refinery.UnitId;
        if (!refinery.IsCompleted)
        {
            EnsureConstruction(world, actor, commands, refinery);
            Goal = AIGoalState.ConstructingRefinery;
            setStatus?.Invoke(AIPlayerStatus.Building);
            LastDecision = $"Constructing refinery ({refinery.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        if (ActiveGoal == AIArmyGoal.BuildRefinery)
        {
            Complete("Refinery goal completed.", setStatus);
            return;
        }

        Harvester? harvester = world.Units.GetArmyUnits(actor.ArmyId).OfType<Harvester>()
            .FirstOrDefault(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying);
        if (harvester is null)
        {
            GameplayDefinition? harvesterType = AIStrategicCatalog.SelectUnit(world, actor.ArmyId,
                new AIProductionNeed(AIUnitRole.Harvester), requireProducer: true);
            Building? producer = harvesterType is null ? null : AIStrategicCatalog.FindAvailableProducer(world, actor.ArmyId, harvesterType);
            if (harvesterType is null || producer is null) { LastDecision = "No available harvest-unit offer."; return; }
            bool queued = producer.ProductionQueue.Orders.Any(order => GameplayCatalog.HasAIRoles(order.UnitTypeId, AIUnitRole.Harvester));
            PurchaseQuote harvestQuote = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, harvesterType.TypeId,
                actor.ArmyId, producer.UnitId));
            if (!queued && !AIResourcePlanner.CanPropose(world, actor.ArmyId, harvestQuote, AIOrderPriority.Economy))
            { LastDecision = $"Waiting for {harvestQuote.FinalPrice} resources for a harvest unit."; return; }
            if (!queued)
                _ = commands.TrainUnitAsync(producer.UnitId, harvesterType.TypeId);
            Goal = AIGoalState.WaitingForHarvester;
            setStatus?.Invoke(AIPlayerStatus.Building);
            LastDecision = queued
                ? "Waiting for a replacement harvester."
                : "Ordered a replacement harvester from the refinery.";
            return;
        }

        if (!_harvestOrderIssued || harvester.HarvestPhase == HarvestPhase.Idle)
        {
            KeyValuePair<Point, TiberiumCell>? target = world.Tiberium.Cells
                .Where(pair => pair.Value.Amount > 0.01f)
                .Where(pair => !world.AIOrderMonitors.TryGetValue(actor.ArmyId, out var monitor) ||
                    !monitor.AvoidHarvestTarget(harvester.UnitId, world.GameGrid.ToWorldPosition(pair.Key, 0)))
                .OrderBy(pair => Vector3.DistanceSquared(
                    harvester.Position,
                    world.GameGrid.ToWorldPosition(pair.Key, harvester.Position.Y)))
                .Select(pair => (KeyValuePair<Point, TiberiumCell>?)pair)
                .FirstOrDefault();
            if (target is null)
            {
                Goal = AIGoalState.WaitingForHarvester;
                setStatus?.Invoke(AIPlayerStatus.Gathering);
                LastDecision = "Harvester ready; waiting for a Tiberium field.";
                return;
            }

            Vector3 harvestPosition = world.GameGrid.ToWorldPosition(target.Value.Key, 0.0f);
            _ = commands.HarvestAsync(harvester.UnitId, harvestPosition);
            _harvestOrderIssued = true;
            Goal = AIGoalState.EconomyOnline;
            setStatus?.Invoke(AIPlayerStatus.Gathering);
            LastDecision = $"Economy online; sent harvester to ({harvestPosition.X:0.0}, {harvestPosition.Z:0.0}).";
            return;
        }

        BuildBaseDefense(actor, world, commands, reactor, setStatus);
    }

    private void BuildBaseDefense(Player actor, GameWorld world, PlayerCommandService commands,
        Building? reactor, Action<AIPlayerStatus>? setStatus)
    {
        Building? barracks = AIStrategicCatalog.FindBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.InfantryProduction, _barracksId);
        if (barracks is null)
        {
            if (WaitForBuildConfirmation(AIGoalState.BarracksBuildRequested,
                "Waiting for the host to confirm the barracks site."))
                return;

            MobileUnit? bulldozer = FindBuilder(world, actor.ArmyId);
            if (bulldozer is null)
            {
                Goal = AIGoalState.FindingBulldozer;
                LastDecision = "Economy is online, but no usable bulldozer was found for the barracks.";
                return;
            }

            GameplayDefinition? selected = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.InfantryProduction, requireAvailable: true);
            if (selected is null) { LastDecision = "No feasible infantry producer offer."; return; }
            string buildingType = selected.TypeId;
            bulldozer = AIStrategicCatalog.FindBuilder(world, actor.ArmyId, buildingType);
            if (bulldozer is null) return;
            PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
                PurchasableType.Building, buildingType, actor.ArmyId));
            int price = quote.FinalPrice;
            if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, quote, AIOrderPriority.Survival))
            {
                Goal = AIGoalState.FindingBarracksSite;
                LastDecision = $"Waiting for {price} resources before building the barracks.";
                return;
            }

            Goal = AIGoalState.FindingBarracksSite;
            Building? preview = BuildingFactory.SpawnBuilding(buildingType, Vector3.Zero, 0, Guid.NewGuid(), actor.Id, price);
            if (preview is null) return;
            PreparePreview(preview, actor);
            Vector3 origin = reactor?.Position ?? bulldozer.Position;
            if (!TryFindBuildingSite(world, preview, origin, 6, 18, out Vector3 position))
            {
                LastDecision = "No valid barracks site found near the base; I will retry.";
                return;
            }

            _barracksId = RequestBuilding(actor, commands, bulldozer, buildingType, position, setStatus);
            Goal = AIGoalState.BarracksBuildRequested;
            LastDecision = $"Requested barracks at ({position.X:0.0}, {position.Z:0.0}).";
            return;
        }

        _barracksId = barracks.UnitId;
        if (!barracks.IsCompleted)
        {
            EnsureConstruction(world, actor, commands, barracks);
            Goal = AIGoalState.ConstructingBarracks;
            setStatus?.Invoke(AIPlayerStatus.Building);
            LastDecision = $"Constructing barracks ({barracks.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        if (!_rallyPointIssued && TryFindRallyPoint(world, barracks, out Vector3 rallyPoint))
        {
            _ = commands.SetRallyPointAsync(barracks.UnitId, rallyPoint);
            _rallyPointIssued = true;
        }

        var defenderNeed = new AIProductionNeed(
            AIUnitRole.Defender | AIUnitRole.AntiInfantry,
            AIMovementDomain.Infantry, AntiInfantry: 1.0f, Defense: 0.5f);
        GameplayDefinition? defenderType = AIStrategicCatalog.SelectUnit(world, actor.ArmyId,
            defenderNeed, requireProducer: true);
        if (defenderType is null)
        {
            LastDecision = "Barracks has no catalog unit suitable for base defense.";
            return;
        }
        Building? defenseProducer = AIStrategicCatalog.FindAvailableProducer(world, actor.ArmyId, defenderType);
        if (defenseProducer is null) return;
        int soldiers = world.Units.GetArmyUnits(actor.ArmyId).Count(unit =>
            unit.ArmyId == actor.ArmyId && !unit.IsDying &&
            GameplayCatalog.HasAIRoles(unit.GameplayTypeId,
                AIUnitRole.Defender | AIUnitRole.AntiInfantry));
        int queued = world.Units.GetArmyUnits(actor.ArmyId).OfType<Building>().Where(b => b.ArmyId == actor.ArmyId && !b.IsDying)
            .SelectMany(b => b.ProductionQueue.Orders).Count(order =>
            GameplayCatalog.HasAIRoles(order.UnitTypeId,
                AIUnitRole.Defender | AIUnitRole.AntiInfantry));
        if (soldiers + queued >= 3)
        {
            if (soldiers < 3)
            {
                Goal = AIGoalState.TrainingSoldiers;
                setStatus?.Invoke(AIPlayerStatus.Building);
                LastDecision = $"Training base defenders ({soldiers}/3 ready, {queued} queued).";
                return;
            }

            ActiveGoal = AIArmyGoal.None;
            Goal = AIGoalState.BaseDefenseReady;
            setStatus?.Invoke(AIPlayerStatus.Active);
            LastDecision = "Base defense ready: three soldiers are guarding the base.";
            return;
        }

        PurchaseQuote soldierQuote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Unit, defenderType.TypeId, actor.ArmyId, defenseProducer.UnitId));
        int soldierPrice = soldierQuote.FinalPrice;
        if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, soldierQuote, AIOrderPriority.Survival))
        {
            Goal = AIGoalState.TrainingSoldiers;
            LastDecision = $"Waiting for {soldierPrice} resources for the next soldier ({soldiers}/3 ready).";
            return;
        }

        _ = commands.TrainUnitAsync(defenseProducer.UnitId, defenderType.TypeId);
        Goal = AIGoalState.TrainingSoldiers;
        setStatus?.Invoke(AIPlayerStatus.Building);
        LastDecision = $"Ordered a base defender ({soldiers}/3 ready, {queued + 1} queued).";
    }

    private static bool TryFindRallyPoint(GameWorld world, Building building, out Vector3 position)
    {
        Point center = world.GameGrid.ToCell(building.Position);
        foreach (Point cell in CandidateCells(center, 3, 7))
        {
            if (!world.GameGrid.Contains(cell)) continue;
            GridCell data = world.GameGrid.GetCell(cell);
            if (!data.HasTerrain || data.IsBlocked || data.ExcludeFromPathfinding ||
                data.AllowedMovement == MovementModes.None || world.GameGrid.GetOccupant(cell) is Building)
                continue;
            position = world.GameGrid.ToWorldPosition(cell, 0.0f);
            position.Y = world.Terrain.GetSurfaceHeight(position.X, position.Z);
            return true;
        }

        position = default;
        return false;
    }

    private void BuildBase(Player actor, GameWorld world, PlayerCommandService commands,
        Action<AIPlayerStatus>? setStatus)
    {
        if (WaitForBuildConfirmation(AIGoalState.BaseBuildRequested,
            "Waiting for the host to confirm the base site."))
            return;

        MobileUnit? bulldozer = FindBuilder(world, actor.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "No usable bulldozer found; I cannot establish a base.";
            return;
        }

        GameplayDefinition? selected = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Base, requireAvailable: true);
        if (selected is null) { LastDecision = "No feasible base offer."; return; }
        string buildingType = selected.TypeId;
        bulldozer = AIStrategicCatalog.FindBuilder(world, actor.ArmyId, buildingType);
        if (bulldozer is null) return;
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, buildingType, actor.ArmyId));
        if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, quote, AIOrderPriority.Survival))
        {
            Goal = AIGoalState.FindingBaseSite;
            LastDecision = $"Waiting for {quote.FinalPrice} resources before building the base.";
            return;
        }

        Goal = AIGoalState.FindingBaseSite;
        Building? preview = BuildingFactory.SpawnBuilding(buildingType, Vector3.Zero, 0, Guid.NewGuid(), actor.Id, quote.FinalPrice);
        if (preview is null) return;
        PreparePreview(preview, actor);
        if (!TryFindBuildingSite(world, preview, bulldozer.Position, 4, 14, out Vector3 position))
        {
            LastDecision = "No valid base site found near the bulldozer; I will retry.";
            return;
        }

        _baseId = RequestBuilding(actor, commands, bulldozer, buildingType, position, setStatus);
        Goal = AIGoalState.BaseBuildRequested;
        LastDecision = $"Requested base at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private void BuildReactor(Player actor, GameWorld world, PlayerCommandService commands,
        Action<AIPlayerStatus>? setStatus)
    {
        if (WaitForBuildConfirmation(AIGoalState.ReactorBuildRequested,
            "Waiting for the host to confirm the reactor site."))
            return;

        MobileUnit? bulldozer = FindBuilder(world, actor.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "No usable bulldozer found; I will try again.";
            return;
        }

        Goal = AIGoalState.FindingReactorSite;
        GameplayDefinition? selected = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Power, requireAvailable: true);
        if (selected is null) { LastDecision = "No feasible power offer."; return; }
        bulldozer = AIStrategicCatalog.FindBuilder(world, actor.ArmyId, selected.TypeId);
        if (bulldozer is null) return;
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new(PurchasableType.Building, selected.TypeId, actor.ArmyId));
        if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, quote, AIOrderPriority.Power)) return;
        Building? preview = BuildingFactory.SpawnBuilding(selected.TypeId, Vector3.Zero, 0, Guid.NewGuid(), actor.Id, quote.FinalPrice);
        if (preview is null) return;
        PreparePreview(preview, actor);
        if (!TryFindBuildingSite(world, preview, bulldozer.Position, 5, 15, out Vector3 position))
        {
            LastDecision = "No valid reactor site found near the bulldozer; I will retry.";
            return;
        }

        _reactorId = RequestBuilding(actor, commands, bulldozer, selected.TypeId, position, setStatus);
        Goal = AIGoalState.ReactorBuildRequested;
        LastDecision = $"Requested reactor at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private void BuildRefinery(Player actor, GameWorld world, PlayerCommandService commands,
        Vector3 reactorPosition, Action<AIPlayerStatus>? setStatus)
    {
        if (WaitForBuildConfirmation(AIGoalState.RefineryBuildRequested,
            "Waiting for the host to confirm the refinery site."))
            return;

        MobileUnit? bulldozer = FindBuilder(world, actor.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "The reactor is ready, but no usable bulldozer was found.";
            return;
        }

        GameplayDefinition? selected = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Economy, requireAvailable: true);
        if (selected is null) { LastDecision = "No feasible economy offer."; return; }
        string buildingType = selected.TypeId;
        bulldozer = AIStrategicCatalog.FindBuilder(world, actor.ArmyId, buildingType);
        if (bulldozer is null) return;
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, buildingType, actor.ArmyId));
        int price = quote.FinalPrice;
        if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, quote, AIOrderPriority.Survival))
        {
            Goal = AIGoalState.FindingRefinerySite;
            LastDecision = $"Waiting for {price} resources before building the refinery.";
            return;
        }

        Goal = AIGoalState.FindingRefinerySite;
        Building? preview = BuildingFactory.SpawnBuilding(buildingType, Vector3.Zero, 0, Guid.NewGuid(), actor.Id, price);
        if (preview is null) return;
        PreparePreview(preview, actor);
        if (!TryFindBuildingSite(world, preview, reactorPosition, 6, 18, out Vector3 position))
        {
            LastDecision = "No valid refinery site found near the reactor; I will retry.";
            return;
        }

        _refineryId = RequestBuilding(actor, commands, bulldozer, buildingType, position, setStatus);
        Goal = AIGoalState.RefineryBuildRequested;
        LastDecision = $"Requested refinery at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private bool WaitForBuildConfirmation(AIGoalState requestedGoal, string message)
    {
        if (Goal != requestedGoal || _requestElapsed >= BuildRequestTimeoutSeconds)
            return false;
        LastDecision = message;
        return true;
    }

    private Guid RequestBuilding(Player actor, PlayerCommandService commands, MobileUnit bulldozer,
        string buildingType, Vector3 position, Action<AIPlayerStatus>? setStatus)
    {
        Guid buildingId = Guid.NewGuid();
        _ = commands.BuildAndConstructAsync(buildingType, position, 0.0f,
            [bulldozer.UnitId], buildingId);
        _requestElapsed = 0.0f;
        setStatus?.Invoke(AIPlayerStatus.Building);
        return commands.LastRequest?.Request.UnitId ?? buildingId;
    }

    private bool NeedsCoreMaintenance(GameWorld world, Guid armyId)
    {
        if (AIStrategicCatalog.FindBuilding(world, armyId, AIStrategicBuildingNeed.Base)?.IsCompleted != true)
        {
            _baseId = null;
            return true;
        }
        if (FindBuilder(world, armyId) is null)
            return true;
        if (AIStrategicCatalog.FindBuilding(world, armyId, AIStrategicBuildingNeed.Economy)?.IsCompleted != true)
        {
            _refineryId = null;
            _harvestOrderIssued = false;
            return true;
        }
        if (AIStrategicCatalog.FindBuilding(world, armyId, AIStrategicBuildingNeed.InfantryProduction)?.IsCompleted != true)
        {
            _barracksId = null;
            _rallyPointIssued = false;
            return true;
        }
        if (!world.Units.GetArmyUnits(armyId).OfType<Harvester>().Any(unit =>
                unit.ArmyId == armyId && !unit.IsDying && unit.HarvestPhase != HarvestPhase.Idle))
            return true;

        int defenders = world.Units.GetArmyUnits(armyId).Count(unit => unit.ArmyId == armyId &&
            !unit.IsDying && GameplayCatalog.HasAIRoles(unit.GameplayTypeId,
                AIUnitRole.Defender | AIUnitRole.AntiInfantry));
        return defenders < 3;
    }

    internal static MobileUnit? FindBuilder(GameWorld world, Guid armyId) => AIStrategicCatalog.FindBuilder(world, armyId);

    private static void EnsureConstruction(
        GameWorld world,
        Player actor,
        PlayerCommandService commands,
        Building constructionSite)
    {
        MobileUnit? builder = AIStrategicCatalog.FindBuilder(world, actor.ArmyId, constructionSite.GameplayTypeId);
        if (builder is null || builder.IsBuilding ||
            builder.TargetBuildingId == constructionSite.UnitId)
            return;

        _ = commands.ConstructAsync([builder.UnitId], constructionSite.UnitId);
    }

    internal static void PreparePreview(Building preview, Player actor)
    {
        preview.SetCreatorPlayer(actor.Id);
        preview.SetArmy(actor.ArmyId);
    }

    private void Complete(string decision, Action<AIPlayerStatus>? setStatus)
    {
        ActiveGoal = AIArmyGoal.None;
        Goal = AIGoalState.EconomyOnline;
        LastDecision = decision;
        setStatus?.Invoke(AIPlayerStatus.Active);
    }

    internal static bool TryFindBuildingSite(GameWorld world, Building preview, Vector3 searchOrigin,
        int minimumRadius, int maximumRadius, out Vector3 position)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.BuildSiteSearch");
        Point center = world.GameGrid.ToCell(searchOrigin);
        foreach (Point cell in CandidateCells(center, minimumRadius, maximumRadius))
        {
            if (!world.GameGrid.Contains(cell) ||
                preview.ArmyId is Guid armyId && world.AIOrderMonitors.TryGetValue(armyId, out var monitor) && monitor.AvoidSite(cell))
                continue;
            Vector3 candidate = world.GameGrid.ToWorldPosition(cell, 0.0f);
            candidate.Y = world.Terrain.GetSurfaceHeight(candidate.X, candidate.Z);
            if (preview.EvaluatePlacement(world, candidate, 0.0f).IsAllowed &&
                HasBuildingSpacing(world, preview, candidate, 0.0f,
                    MinimumBuildingSpacingCells))
            {
                position = candidate;
                return true;
            }
        }

        position = default;
        return false;
    }

    internal static bool HasBuildingSpacing(
        GameWorld world,
        Building preview,
        Vector3 position,
        float rotationDegrees,
        int spacingCells)
    {
        if (spacingCells <= 0)
            return true;

        IReadOnlyList<Point> candidateCells = world.GameGrid.GetFootprintCells(
            preview, position, rotationDegrees);
        foreach (Building existing in world.Units.Units.OfType<Building>())
        {
            if (existing == preview || existing.IsDying)
                continue;

            float existingYaw = MathHelper.ToDegrees(MathF.Atan2(
                -existing.Transform.Forward.X, -existing.Transform.Forward.Z));
            IReadOnlyList<Point> existingCells = world.GameGrid.GetFootprintCells(
                existing, existing.Position, existingYaw);
            foreach (Point candidate in candidateCells)
                foreach (Point occupied in existingCells)
                    if (Math.Abs(candidate.X - occupied.X) <= spacingCells &&
                        Math.Abs(candidate.Y - occupied.Y) <= spacingCells)
                        return false;
        }
        return true;
    }

    internal static IEnumerable<Point> CandidateCells(Point center, int minimumRadius, int maximumRadius)
    {
        for (int radius = minimumRadius; radius <= maximumRadius; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                yield return new Point(center.X + x, center.Y - radius);
                yield return new Point(center.X + x, center.Y + radius);
            }
            for (int y = -radius + 1; y < radius; y++)
            {
                yield return new Point(center.X - radius, center.Y + y);
                yield return new Point(center.X + radius, center.Y + y);
            }
        }
    }
}

/// <summary>Compatibility facade for autonomous AI players.</summary>
public sealed class AIController
{
    private const float ScoutReplacementRetrySeconds = 3.0f;

    private enum SquadCyclePhase
    {
        InitialPreparation,
        Combat,
        Reinforcing,
        Recovering,
        Reassembling
    }

    private readonly ArmyGoalController _goals = new();
    private AIOrderProgressMonitor? _orderMonitor;
    private AIOrderQueue? _orderQueue;
    private bool _tacticsActivated;
    private ScoutingController? _scouting;
    private AIBaseDefenseController? _baseDefense;
    private AISquadPreparationController? _squadPreparation;
    private AISquadAssaultController? _squadAssault;
    private AISquadRecoveryController? _squadRecovery;
    private AIArmoredSupportController? _armoredSupport;
    private AIInfrastructureController? _infrastructure;
    private AIDefensePlanner? _defensePlanner;
    private AIThreatAssessment? _threatAssessment;
    private SquadCyclePhase _squadCyclePhase;
    private AIStrategyProfile? _strategyProfile;
    private Guid? _scoutId;
    private float _scoutReplacementElapsed = ScoutReplacementRetrySeconds;
    private string? _scoutingDecision;

    public AIGoalState Goal => _goals.Goal == AIGoalState.BaseDefenseReady
        ? AIGoalState.Scouting
        : _goals.Goal;
    public string LastDecision
    {
        get
        {
            string decision = _scoutingDecision ?? _goals.LastDecision;
            if (_orderMonitor?.LastDecision is string recovery) decision += $" | {recovery}";
            if (_orderQueue is not null) decision += $" | {_orderQueue.Diagnostic}";
            return decision;
        }
    }
    public AIOrderQueue? OrderQueue => _orderQueue;
    public AIStrategyProfile? StrategyProfile => _strategyProfile;
    public AIThreatSnapshot Threats => _threatAssessment?.Current ?? AIThreatSnapshot.Baseline;

    public void RecordCombatLoss(Unit lostUnit, Unit? attacker)
    {
        AICombatContext context = lostUnit.UnitId == _scoutId
            ? AICombatContext.Scouting
            : lostUnit is Harvester
                ? AICombatContext.ResourceOperation
                : _baseDefense?.IsEngaging == true
                    ? AICombatContext.BaseDefense
                    : _squadAssault?.HasActiveMission == true
                        ? AICombatContext.Offensive
                        : AICombatContext.Unknown;
        _threatAssessment?.RecordLoss(lostUnit, attacker, context);
    }

    public void BeginMatch(int matchSeed, Guid armyId)
    {
        _orderQueue?.Dispose();
        _orderQueue = null;
        _orderMonitor?.Dispose();
        _orderMonitor = null;
        _tacticsActivated = false;
        _scouting?.Dispose();
        _scouting = null;
        _baseDefense = null;
        _squadPreparation = null;
        _squadAssault = null;
        _squadRecovery = null;
        _armoredSupport = null;
        _infrastructure = null;
        _defensePlanner = null;
        _threatAssessment = new AIThreatAssessment(armyId);
        _squadCyclePhase = SquadCyclePhase.InitialPreparation;
        _scoutId = null;
        _scoutReplacementElapsed = ScoutReplacementRetrySeconds;
        _scoutingDecision = null;
        _strategyProfile = AIStrategyProfile.Create(matchSeed, armyId);
        _goals.Start(AIArmyGoal.EstablishEconomy);
    }

    public void Update(GameTime gameTime, AIPlayer ai, GameWorld world, NetworkHandler network)
    {
        if (ai.Status == AIPlayerStatus.Idle || !network.IsHost)
            return;

        if (!world.AIOrderQueues.TryGetValue(ai.Player.ArmyId, out var registeredQueue) || registeredQueue != _orderQueue)
            _orderQueue = new AIOrderQueue(world, ai.Player, network);
        _orderQueue.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        using IDisposable orders = _orderQueue.Collect();
        _orderMonitor ??= new AIOrderProgressMonitor(world, ai.Player, network);
        _orderMonitor.Update(gameTime);
        _orderQueue.Run(AIOrderPriority.Survival, () =>
            _goals.Update(gameTime, ai.Player, world, network, ai.SetStatus));
        bool coreReady = _goals.Goal == AIGoalState.BaseDefenseReady;
        _orderQueue.Budget.MinimumPurchasePriority = coreReady ? AIOrderPriority.Expansion : AIOrderPriority.Survival;
        // Initial setup unlocks tactics once per match. Maintenance must never
        // suspend existing defenders, scouts or a squad's combat/retreat state.
        _tacticsActivated |= coreReady;
        if (!_tacticsActivated)
            return;

        _strategyProfile ??= AIStrategyProfile.Create(0, ai.Player.ArmyId);
        _threatAssessment ??= new AIThreatAssessment(ai.Player.ArmyId);
        _threatAssessment.Update(gameTime, world);
        _baseDefense ??= new AIBaseDefenseController(
            world, ai.Player.Id, ai.Player.ArmyId, network, _strategyProfile.DefenseRadiusInCells);
        _squadPreparation ??= new AISquadPreparationController(
            world, ai.Player.Id, ai.Player.ArmyId, network, _strategyProfile, _threatAssessment);
        _squadAssault ??= new AISquadAssaultController(
            world, ai.Player.Id, ai.Player.ArmyId, network, _strategyProfile);
        _squadRecovery ??= new AISquadRecoveryController(
            world, ai.Player.Id, ai.Player.ArmyId, network);
        _armoredSupport ??= new AIArmoredSupportController(
            world, ai.Player, network, _strategyProfile, _threatAssessment);
        _infrastructure ??= new AIInfrastructureController(world, ai.Player, network);
        _defensePlanner ??= new AIDefensePlanner(
            world, ai.Player, network, _threatAssessment);

        bool infrastructureUpdated = false;
        if (coreReady && _infrastructure.RequiresImmediatePower)
        {
            _orderQueue.Run(AIOrderPriority.Power, () => _infrastructure.Update(gameTime));
            infrastructureUpdated = true;
        }

        _scouting ??= new ScoutingController(world, ai.Player.Id, network);
        _scoutReplacementElapsed += Math.Max(0.0f,
            (float)gameTime.ElapsedGameTime.TotalSeconds);
        MobileUnit? scout = _scoutId is Guid scoutId
            ? world.Units.FindById(scoutId) as MobileUnit
            : null;
        if (scout is null || scout.IsDying || scout.IsEmbarked || scout.ArmyId != ai.Player.ArmyId)
        {
            if (scout is not null)
                _scouting.Stop([scout]);

            scout = world.Units.GetArmyUnits(ai.Player.ArmyId).OfType<MobileUnit>()
                .Where(unit => unit.ArmyId == ai.Player.ArmyId && !unit.IsDying && !unit.IsEmbarked &&
                    GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Scout))
                .OrderBy(unit => unit.UnitId)
                .FirstOrDefault();
            _scoutId = scout?.UnitId;
            if (scout is null)
            {
                _scoutingDecision = coreReady
                    ? TryRequestReplacementScout(ai.Player, world, network)
                    : "Scouting paused; core reconstruction takes priority over a replacement scout.";
            }
            else
            {
                _scouting.Start([scout]);
                int defenders = world.Units.GetArmyUnits(ai.Player.ArmyId).Count(unit =>
                    unit.UnitId != scout.UnitId && GameplayCatalog.HasAIRoles(unit.GameplayTypeId,
                        AIUnitRole.Defender) &&
                    unit.ArmyId == ai.Player.ArmyId && !unit.IsDying && !unit.IsEmbarked);
                _scoutingDecision = $"Scout {scout.UnitId.ToString()[..8]} is exploring unknown terrain; " +
                    $"{defenders} soldier(s) remain at the base.";
            }
        }

        _scouting.Update(gameTime);
        _baseDefense.Update(gameTime, _scoutId);
        // Keep discretionary production/expansion from taking the maintenance
        // plan's resources or builder. Confirmed host jobs continue normally.
        if (coreReady)
            _orderQueue.Run(AIOrderPriority.Production, () => _armoredSupport.Update(gameTime));
        // Completed production infrastructure is enough to begin expansion.
        // Combat losses and a pending vehicle order must not suspend air research.
        if (coreReady && _armoredSupport.HasOperationalFactory)
        {
            _orderQueue.Run(AIOrderPriority.Defense, () => _defensePlanner.Update(gameTime),
                urgent: _threatAssessment.Current.AntiAirNeed >= AIDefensePlanner.AirThreatThreshold);
            if ((!_defensePlanner.IsBusy || _infrastructure.HasActivePlan) && !infrastructureUpdated)
                _orderQueue.Run(AIOrderPriority.Expansion, () => _infrastructure.Update(gameTime));
        }
        if (_squadCyclePhase == SquadCyclePhase.Combat &&
            _squadAssault.State == AISquadAssaultState.MissionComplete)
        {
            _squadPreparation.BeginReinforcement();
            _squadCyclePhase = SquadCyclePhase.Reinforcing;
        }

        if (_squadCyclePhase is SquadCyclePhase.InitialPreparation or
            SquadCyclePhase.Reinforcing or SquadCyclePhase.Reassembling)
        {
            if (coreReady && !_squadPreparation.IsReady)
                _orderQueue.Run(AIOrderPriority.Production, () => _squadPreparation.Update(gameTime, _scoutId));
            if (_squadPreparation.IsReady)
            {
                if (_squadCyclePhase == SquadCyclePhase.Reinforcing && _squadRecovery.Begin())
                    _squadCyclePhase = SquadCyclePhase.Recovering;
                else if (_squadCyclePhase == SquadCyclePhase.Reassembling)
                {
                    _squadRecovery.Reset();
                    _squadAssault.BeginNextMission();
                    _squadCyclePhase = SquadCyclePhase.Combat;
                }
                else if (_squadCyclePhase == SquadCyclePhase.InitialPreparation)
                    _squadCyclePhase = SquadCyclePhase.Combat;
            }
        }
        else if (_squadCyclePhase == SquadCyclePhase.Recovering)
        {
            _squadRecovery.Update(gameTime);
            if (_squadRecovery.IsFinished)
            {
                _squadPreparation.BeginReinforcement();
                _squadCyclePhase = SquadCyclePhase.Reassembling;
            }
        }

        if (_squadCyclePhase == SquadCyclePhase.Combat &&
            (_squadPreparation.IsReady || _squadAssault.HasActiveMission) && !_baseDefense.IsEngaging)
        {
            _squadAssault.ReservedScoutId = _scoutId;
            _squadAssault.Update(gameTime);
        }
        if (_baseDefense.IsEngaging)
            _scoutingDecision = _baseDefense.LastDecision;
        else if (_squadCyclePhase is SquadCyclePhase.Reinforcing or SquadCyclePhase.Reassembling ||
            _squadCyclePhase == SquadCyclePhase.InitialPreparation && !_squadPreparation.IsReady)
            _scoutingDecision = _squadPreparation.LastDecision;
        else if (_squadCyclePhase == SquadCyclePhase.Recovering)
            _scoutingDecision = _squadRecovery.LastDecision;
        else if (_squadCyclePhase == SquadCyclePhase.Combat)
            _scoutingDecision = _squadAssault.LastDecision;
        else if (scout is not null)
            _scoutingDecision = $"Scout {scout.UnitId.ToString()[..8]} is exploring unknown terrain; " +
                "the first squad is assembled at the base and awaits orders.";
        if (!coreReady)
            _scoutingDecision = $"Core reconstruction: {_goals.LastDecision} | {_scoutingDecision}";
        else
        {
            if (!_armoredSupport.IsReady)
                _scoutingDecision = $"{_scoutingDecision} | {_armoredSupport.LastDecision}";
            if (_defensePlanner.IsBusy)
                _scoutingDecision = $"{_scoutingDecision} | {_defensePlanner.LastDecision}";
            if (_infrastructure.HasActivePlan || _armoredSupport.HasOperationalFactory && !_infrastructure.IsAirSupportReady)
                _scoutingDecision = $"{_scoutingDecision} | {_infrastructure.LastDecision}";
        }
    }

    private string TryRequestReplacementScout(Player actor, GameWorld world, NetworkHandler network)
    {
        Building[] producers = world.Units.GetArmyUnits(actor.ArmyId).OfType<Building>()
            .Where(building => building.ArmyId == actor.ArmyId && building.IsCompleted &&
                !building.IsDying)
            .OrderBy(building => building.UnitId)
            .ToArray();
        bool scoutQueued = producers.Any(producer => producer.ProductionQueue.Orders.Any(order =>
            GameplayCatalog.HasAIRoles(order.UnitTypeId, AIUnitRole.Scout)));
        if (scoutQueued)
            return "Scouting paused; waiting for the replacement scout already in production.";

        GameplayDefinition? replacement = SelectScoutReplacement(
            producers.Select(producer => producer.GameplayTypeId));
        if (replacement is null)
            return "Scouting paused; no completed building can currently produce a scout.";

        Building? producer = producers.FirstOrDefault(candidate =>
            replacement.Producers.Any(registered => string.Equals(
                registered.TypeId, candidate.GameplayTypeId, StringComparison.OrdinalIgnoreCase)));
        if (producer is null)
            return "Scouting paused; no completed producer is available for a replacement scout.";

        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Unit, replacement.TypeId, actor.ArmyId, producer.UnitId));
        if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, quote))
            return $"Scouting paused; waiting for {quote.FinalPrice} resources for a replacement " +
                $"{replacement.DisplayName}.";
        if (_scoutReplacementElapsed < ScoutReplacementRetrySeconds)
            return $"Scouting paused; preparing a replacement {replacement.DisplayName}.";

        _ = new PlayerCommandService(network, actor.Id)
            .TrainUnitAsync(producer.UnitId, replacement.TypeId);
        _scoutReplacementElapsed = 0.0f;
        return $"Scouting paused; requested replacement {replacement.DisplayName}.";
    }

    private static GameplayDefinition? SelectScoutReplacement(
        IEnumerable<string> availableProducerTypeIds)
    {
        HashSet<string> producers = availableProducerTypeIds
            .Select(GameplayCatalog.Canonicalize)
            .Where(typeId => !string.IsNullOrWhiteSpace(typeId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GameplayCatalog.All
            .Where(definition => definition.Type == PurchasableType.Unit &&
                definition.AI is AIUnitMetadata ai &&
                (ai.Roles & AIUnitRole.Scout) != 0 &&
                definition.Producers.Any(producer => producers.Contains(producer.TypeId)))
            .OrderBy(definition => definition.BasePrice)
            .ThenByDescending(definition => definition.AI!.Scouting)
            .ThenBy(definition => definition.TypeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
