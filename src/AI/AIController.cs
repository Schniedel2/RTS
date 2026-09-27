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
    FindingReactorSite,
    ReactorBuildRequested,
    ConstructingReactor,
    FindingRefinerySite,
    RefineryBuildRequested,
    ConstructingRefinery,
    WaitingForHarvester,
    EconomyOnline
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
    private Guid? _reactorId;
    private Guid? _refineryId;

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
        _reactorId = null;
        _refineryId = null;
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
            Goal is AIGoalState.WaitingForMatch or AIGoalState.EconomyOnline)
            return;

        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += seconds;
        if (Goal is AIGoalState.ReactorBuildRequested or AIGoalState.RefineryBuildRequested)
            _requestElapsed += seconds;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;
        var commands = new PlayerCommandService(network, actor.Id);

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
        Goal = AIGoalState.EconomyOnline;
        setStatus?.Invoke(AIPlayerStatus.Gathering);
        LastDecision = $"Economy online; sent harvester to ({harvestPosition.X:0.0}, {harvestPosition.Z:0.0}).";
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
        var preview = new Reaktor(Vector3.Zero, Guid.NewGuid(), BuildingFactory.GetPurchasePrice("Reaktor"));
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
        int price = BuildingFactory.GetPurchasePrice(buildingType);
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
    public AIGoalState Goal => _goals.Goal;
    public string LastDecision => _goals.LastDecision;
    public void BeginMatch() => _goals.Start(AIArmyGoal.EstablishEconomy);
    public void Update(GameTime gameTime, AIPlayer ai, GameWorld world, NetworkHandler network)
    {
        if (ai.Status != AIPlayerStatus.Idle)
            _goals.Update(gameTime, ai.Player, world, network, ai.SetStatus);
    }
}
