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

/// <summary>
/// Deliberately small host-side AI plan: construct one reactor followed by one
/// Tiberium refinery through the same requests a human player sends.
/// </summary>
public sealed class AIController
{
    private const float ThinkIntervalSeconds = 1.0f;
    private const float BuildRequestTimeoutSeconds = 3.0f;
    private float _thinkElapsed;
    private float _requestElapsed;
    private Guid? _reactorId;
    private Guid? _refineryId;

    public AIGoalState Goal { get; private set; } = AIGoalState.WaitingForMatch;
    public string LastDecision { get; private set; } = "Waiting for game-start.";

    public void BeginMatch()
    {
        Goal = AIGoalState.FindingBulldozer;
        LastDecision = "Match started; looking for my bulldozer.";
        _thinkElapsed = ThinkIntervalSeconds;
        _requestElapsed = 0.0f;
        _reactorId = null;
        _refineryId = null;
    }

    public void Update(GameTime gameTime, AIPlayer ai, GameWorld world, NetworkHandler network)
    {
        if (!network.IsHost || ai.Status == AIPlayerStatus.Idle ||
            Goal is AIGoalState.WaitingForMatch or AIGoalState.EconomyOnline)
            return;

        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += seconds;
        if (Goal is AIGoalState.ReactorBuildRequested or AIGoalState.RefineryBuildRequested)
            _requestElapsed += seconds;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;
        var commands = new PlayerCommandService(network, ai.Id);

        Building? reactor = FindOwnedBuilding<Reaktor>(world, ai.Player.ArmyId, _reactorId);
        if (reactor is null)
        {
            BuildReactor(ai, world, commands);
            return;
        }

        _reactorId = reactor.UnitId;
        if (!reactor.IsCompleted)
        {
            Goal = AIGoalState.ConstructingReactor;
            ai.SetStatus(AIPlayerStatus.Building);
            LastDecision = $"Constructing reactor ({reactor.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        Building? refinery = FindOwnedBuilding<TiberiumRefinery>(world, ai.Player.ArmyId, _refineryId);
        if (refinery is null)
        {
            BuildRefinery(ai, world, commands, reactor.Position);
            return;
        }

        _refineryId = refinery.UnitId;
        if (!refinery.IsCompleted)
        {
            Goal = AIGoalState.ConstructingRefinery;
            ai.SetStatus(AIPlayerStatus.Building);
            LastDecision = $"Constructing refinery ({refinery.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        Harvester? harvester = world.Units.Units.OfType<Harvester>()
            .FirstOrDefault(unit => unit.ArmyId == ai.Player.ArmyId && !unit.IsDying);
        if (harvester is null)
        {
            Goal = AIGoalState.WaitingForHarvester;
            ai.SetStatus(AIPlayerStatus.Building);
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
            ai.SetStatus(AIPlayerStatus.Gathering);
            LastDecision = "Harvester ready; waiting for a Tiberium field.";
            return;
        }

        Vector3 harvestPosition = world.GameGrid.ToWorldPosition(target.Value.Key, 0.0f);
        _ = commands.HarvestAsync(harvester.UnitId, harvestPosition);
        Goal = AIGoalState.EconomyOnline;
        ai.SetStatus(AIPlayerStatus.Gathering);
        LastDecision = $"Economy online; sent harvester to ({harvestPosition.X:0.0}, {harvestPosition.Z:0.0}).";
    }

    private void BuildReactor(AIPlayer ai, GameWorld world, PlayerCommandService commands)
    {
        if (WaitForBuildConfirmation(AIGoalState.ReactorBuildRequested,
            "Waiting for the host to confirm the reactor site."))
            return;

        GDIBulldozer? bulldozer = FindBulldozer(world, ai.Player.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "No usable bulldozer found; I will try again.";
            return;
        }

        Goal = AIGoalState.FindingReactorSite;
        var preview = new Reaktor(Vector3.Zero, Guid.NewGuid(), BuildingFactory.GetPurchasePrice("Reaktor"));
        PreparePreview(preview, ai);
        if (!TryFindBuildingSite(world, preview, bulldozer.Position, 5, 15, out Vector3 position))
        {
            LastDecision = "No valid reactor site found near the bulldozer; I will retry.";
            return;
        }

        _reactorId = RequestBuilding(ai, commands, bulldozer, "Reaktor", position);
        Goal = AIGoalState.ReactorBuildRequested;
        LastDecision = $"Requested reactor at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private void BuildRefinery(AIPlayer ai, GameWorld world, PlayerCommandService commands, Vector3 reactorPosition)
    {
        if (WaitForBuildConfirmation(AIGoalState.RefineryBuildRequested,
            "Waiting for the host to confirm the refinery site."))
            return;

        GDIBulldozer? bulldozer = FindBulldozer(world, ai.Player.ArmyId);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "The reactor is ready, but no usable bulldozer was found.";
            return;
        }

        const string buildingType = "Tiberium-Refinery";
        int price = BuildingFactory.GetPurchasePrice(buildingType);
        Army? army = Globals.Game.Armies.Find(ai.Player.ArmyId);
        if (army is null || army.Resources < price)
        {
            Goal = AIGoalState.FindingRefinerySite;
            LastDecision = $"Waiting for {price} resources before building the refinery.";
            return;
        }

        Goal = AIGoalState.FindingRefinerySite;
        var preview = new TiberiumRefinery(Vector3.Zero, Guid.NewGuid(), purchasePrice: price);
        PreparePreview(preview, ai);
        if (!TryFindBuildingSite(world, preview, reactorPosition, 6, 18, out Vector3 position))
        {
            LastDecision = "No valid refinery site found near the reactor; I will retry.";
            return;
        }

        _refineryId = RequestBuilding(ai, commands, bulldozer, buildingType, position);
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

    private Guid RequestBuilding(AIPlayer ai, PlayerCommandService commands, GDIBulldozer bulldozer,
        string buildingType, Vector3 position)
    {
        Guid buildingId = Guid.NewGuid();
        _ = commands.BuildAndConstructAsync(buildingType, position, 0.0f,
            [bulldozer.UnitId], buildingId);
        _requestElapsed = 0.0f;
        ai.SetStatus(AIPlayerStatus.Building);
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

    private static void PreparePreview(Building preview, AIPlayer ai)
    {
        preview.SetCreatorPlayer(ai.Id);
        preview.SetArmy(ai.Player.ArmyId);
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
