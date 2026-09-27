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
        if (!network.IsHost || ActiveGoal == AIArmyGoal.None ||
            Goal is AIGoalState.WaitingForMatch or AIGoalState.BaseDefenseReady)
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

        GDIBase? homeBase = FindOwnedBuilding<GDIBase>(world, actor.ArmyId, _baseId);
        if (homeBase is null)
        {
            BuildBase(actor, world, commands, setStatus);
            return;
        }

        _baseId = homeBase.UnitId;
        if (!homeBase.IsCompleted)
        {
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

        Building? reactor = FindOwnedBuilding<Reaktor>(world, actor.ArmyId, _reactorId);
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

        Building? refinery = FindOwnedBuilding<TiberiumRefinery>(world, actor.ArmyId, _refineryId);
        if (refinery is null)
        {
            Vector3 origin = reactor?.Position ?? FindBulldozer(world, actor.ArmyId)?.Position ?? Vector3.Zero;
            BuildRefinery(actor, world, commands, origin, setStatus);
            return;
        }

        _refineryId = refinery.UnitId;
        if (!refinery.IsCompleted)
        {
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

        Harvester? harvester = world.Units.Units.OfType<Harvester>()
            .FirstOrDefault(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying);
        if (harvester is null)
        {
            Goal = AIGoalState.WaitingForHarvester;
            setStatus?.Invoke(AIPlayerStatus.Building);
            LastDecision = "Refinery complete; waiting for its included harvester.";
            return;
        }

        if (!_harvestOrderIssued)
        {
            KeyValuePair<Point, TiberiumCell>? target = world.Tiberium.Cells
                .Where(pair => pair.Value.Amount > 0.01f)
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
        GDIBarracks? barracks = FindOwnedBuilding<GDIBarracks>(world, actor.ArmyId, _barracksId);
        if (barracks is null)
        {
            if (WaitForBuildConfirmation(AIGoalState.BarracksBuildRequested,
                "Waiting for the host to confirm the barracks site."))
                return;

            GDIBulldozer? bulldozer = FindBulldozer(world, actor.ArmyId);
            if (bulldozer is null)
            {
                Goal = AIGoalState.FindingBulldozer;
                LastDecision = "Economy is online, but no usable bulldozer was found for the barracks.";
                return;
            }

            const string buildingType = "GDI-Barracks";
            PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
                PurchasableType.Building, buildingType, actor.ArmyId));
            int price = quote.FinalPrice;
            Army? army = Globals.Game.Armies.Find(actor.ArmyId);
            if (army is null || army.Resources < price)
            {
                Goal = AIGoalState.FindingBarracksSite;
                LastDecision = $"Waiting for {price} resources before building the barracks.";
                return;
            }

            Goal = AIGoalState.FindingBarracksSite;
            var preview = new GDIBarracks(Vector3.Zero, Guid.NewGuid(), price);
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

        int soldiers = world.Units.Units.Count(unit =>
            unit is Gunner && unit.ArmyId == actor.ArmyId && !unit.IsDying);
        int queued = barracks.ProductionQueue.Orders.Count(order =>
            string.Equals(order.UnitTypeId, "gunner", StringComparison.OrdinalIgnoreCase));
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
            PurchasableType.Unit, "gunner", actor.ArmyId, barracks.UnitId));
        int soldierPrice = soldierQuote.FinalPrice;
        Army? ownerArmy = Globals.Game.Armies.Find(actor.ArmyId);
        if (ownerArmy is null || ownerArmy.Resources < soldierPrice)
        {
            Goal = AIGoalState.TrainingSoldiers;
            LastDecision = $"Waiting for {soldierPrice} resources for the next soldier ({soldiers}/3 ready).";
            return;
        }

        _ = commands.TrainUnitAsync(barracks.UnitId, "gunner");
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

        GDIBulldozer? bulldozer = FindBulldozer(world, actor.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "No usable bulldozer found; I cannot establish a base.";
            return;
        }

        const string buildingType = "GDI-Base";
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, buildingType, actor.ArmyId));
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (!quote.IsAvailable || army is null || !quote.CanAfford(army.Resources))
        {
            Goal = AIGoalState.FindingBaseSite;
            LastDecision = $"Waiting for {quote.FinalPrice} resources before building the base.";
            return;
        }

        Goal = AIGoalState.FindingBaseSite;
        var preview = new GDIBase(Vector3.Zero, Guid.NewGuid(), quote.FinalPrice);
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

        GDIBulldozer? bulldozer = FindBulldozer(world, actor.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "No usable bulldozer found; I will try again.";
            return;
        }

        Goal = AIGoalState.FindingReactorSite;
        int price = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, "Reaktor", actor.ArmyId)).FinalPrice;
        var preview = new Reaktor(Vector3.Zero, Guid.NewGuid(), price);
        PreparePreview(preview, actor);
        if (!TryFindBuildingSite(world, preview, bulldozer.Position, 5, 15, out Vector3 position))
        {
            LastDecision = "No valid reactor site found near the bulldozer; I will retry.";
            return;
        }

        _reactorId = RequestBuilding(actor, commands, bulldozer, "Reaktor", position, setStatus);
        Goal = AIGoalState.ReactorBuildRequested;
        LastDecision = $"Requested reactor at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private void BuildRefinery(Player actor, GameWorld world, PlayerCommandService commands,
        Vector3 reactorPosition, Action<AIPlayerStatus>? setStatus)
    {
        if (WaitForBuildConfirmation(AIGoalState.RefineryBuildRequested,
            "Waiting for the host to confirm the refinery site."))
            return;

        GDIBulldozer? bulldozer = FindBulldozer(world, actor.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "The reactor is ready, but no usable bulldozer was found.";
            return;
        }

        const string buildingType = "Tiberium-Refinery";
        int price = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, buildingType, actor.ArmyId)).FinalPrice;
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (army is null || army.Resources < price)
        {
            Goal = AIGoalState.FindingRefinerySite;
            LastDecision = $"Waiting for {price} resources before building the refinery.";
            return;
        }

        Goal = AIGoalState.FindingRefinerySite;
        var preview = new TiberiumRefinery(Vector3.Zero, Guid.NewGuid(), purchasePrice: price);
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

    private Guid RequestBuilding(Player actor, PlayerCommandService commands, GDIBulldozer bulldozer,
        string buildingType, Vector3 position, Action<AIPlayerStatus>? setStatus)
    {
        Guid buildingId = Guid.NewGuid();
        _ = commands.BuildAndConstructAsync(buildingType, position, 0.0f,
            [bulldozer.UnitId], buildingId);
        _requestElapsed = 0.0f;
        setStatus?.Invoke(AIPlayerStatus.Building);
        return buildingId;
    }

    private static TBuilding? FindOwnedBuilding<TBuilding>(GameWorld world, Guid armyId,
        Guid? preferredId) where TBuilding : Building
    {
        if (preferredId is Guid id && world.Units.FindById(id) is TBuilding preferred && !preferred.IsDying)
            return preferred;
        return world.Units.Units.OfType<TBuilding>()
            .FirstOrDefault(unit => unit.ArmyId == armyId && !unit.IsDying);
    }

    private static GDIBulldozer? FindBulldozer(GameWorld world, Guid armyId) =>
        world.Units.Units.OfType<GDIBulldozer>()
            .FirstOrDefault(unit => unit.ArmyId == armyId && !unit.IsDying);

    private static void PreparePreview(Building preview, Player actor)
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

    private static bool TryFindBuildingSite(GameWorld world, Building preview, Vector3 searchOrigin,
        int minimumRadius, int maximumRadius, out Vector3 position)
    {
        Point center = world.GameGrid.ToCell(searchOrigin);
        foreach (Point cell in CandidateCells(center, minimumRadius, maximumRadius))
        {
            if (!world.GameGrid.Contains(cell))
                continue;
            Vector3 candidate = world.GameGrid.ToWorldPosition(cell, 0.0f);
            candidate.Y = world.Terrain.GetSurfaceHeight(candidate.X, candidate.Z);
            if (preview.EvaluatePlacement(world, candidate, 0.0f).IsAllowed)
            {
                position = candidate;
                return true;
            }
        }

        position = default;
        return false;
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
    private readonly ArmyGoalController _goals = new();
    private ScoutingController? _scouting;
    private Guid? _scoutId;
    private string? _scoutingDecision;

    public AIGoalState Goal => _goals.Goal == AIGoalState.BaseDefenseReady
        ? AIGoalState.Scouting
        : _goals.Goal;
    public string LastDecision => _scoutingDecision ?? _goals.LastDecision;

    public void BeginMatch()
    {
        _scouting = null;
        _scoutId = null;
        _scoutingDecision = null;
        _goals.Start(AIArmyGoal.EstablishEconomy);
    }

    public void Update(GameTime gameTime, AIPlayer ai, GameWorld world, NetworkHandler network)
    {
        if (ai.Status == AIPlayerStatus.Idle || !network.IsHost)
            return;

        _goals.Update(gameTime, ai.Player, world, network, ai.SetStatus);
        if (_goals.Goal != AIGoalState.BaseDefenseReady)
            return;

        _scouting ??= new ScoutingController(world, ai.Player.Id, network);
        Gunner? scout = _scoutId is Guid scoutId
            ? world.Units.FindById(scoutId) as Gunner
            : null;
        if (scout is null || scout.IsDying || scout.IsEmbarked || scout.ArmyId != ai.Player.ArmyId)
        {
            if (scout is not null)
                _scouting.Stop([scout]);

            scout = world.Units.Units.OfType<Gunner>()
                .Where(unit => unit.ArmyId == ai.Player.ArmyId && !unit.IsDying && !unit.IsEmbarked)
                .OrderBy(unit => unit.UnitId)
                .FirstOrDefault();
            _scoutId = scout?.UnitId;
            if (scout is null)
            {
                _scoutingDecision = "Base defense ready; waiting for a soldier who can scout.";
                return;
            }

            _scouting.Start([scout]);
            int defenders = world.Units.Units.Count(unit =>
                unit is Gunner && unit.UnitId != scout.UnitId &&
                unit.ArmyId == ai.Player.ArmyId && !unit.IsDying && !unit.IsEmbarked);
            _scoutingDecision = $"Gunner {scout.UnitId.ToString()[..8]} is scouting unexplored terrain; " +
                $"{defenders} soldier(s) remain at the base.";
        }

        _scouting.Update(gameTime);
    }
}
