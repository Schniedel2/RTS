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
    BuildRequested,
    ConstructingReactor,
    ReactorComplete
}

/// <summary>
/// First deliberately small host-side AI plan: construct exactly one reactor
/// through the same requests a human player sends.
/// </summary>
public sealed class AIController
{
    private const float ThinkIntervalSeconds = 1.0f;
    private const float BuildRequestTimeoutSeconds = 3.0f;
    private float _thinkElapsed;
    private float _requestElapsed;
    private Guid? _reactorId;

    public AIGoalState Goal { get; private set; } = AIGoalState.WaitingForMatch;
    public string LastDecision { get; private set; } = "Waiting for game-start.";

    public void BeginMatch()
    {
        Goal = AIGoalState.FindingBulldozer;
        LastDecision = "Match started; looking for my bulldozer.";
        _thinkElapsed = ThinkIntervalSeconds;
        _requestElapsed = 0.0f;
        _reactorId = null;
    }

    public void Update(GameTime gameTime, AIPlayer ai, GameWorld world, NetworkHandler network)
    {
        if (!network.IsHost || ai.Status == AIPlayerStatus.Idle || Goal == AIGoalState.WaitingForMatch ||
            Goal == AIGoalState.ReactorComplete)
            return;

        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += seconds;
        if (Goal == AIGoalState.BuildRequested)
            _requestElapsed += seconds;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        Building? reactor = _reactorId is Guid reactorId
            ? world.Units.FindById(reactorId) as Building
            : world.Units.Units.OfType<Reaktor>().FirstOrDefault(unit => unit.ArmyId == ai.Player.ArmyId && !unit.IsDying);
        if (reactor is not null)
        {
            _reactorId = reactor.UnitId;
            if (reactor.IsCompleted)
            {
                Goal = AIGoalState.ReactorComplete;
                ai.SetStatus(AIPlayerStatus.Active);
                LastDecision = "First goal complete: reactor constructed.";
            }
            else
            {
                Goal = AIGoalState.ConstructingReactor;
                ai.SetStatus(AIPlayerStatus.Building);
                LastDecision = $"Constructing reactor ({reactor.ConstructionPercentage * 100.0f:0}%).";
            }
            return;
        }

        if (Goal == AIGoalState.BuildRequested && _requestElapsed < BuildRequestTimeoutSeconds)
        {
            LastDecision = "Waiting for the host to confirm the reactor site.";
            return;
        }

        GDIBulldozer? bulldozer = world.Units.Units.OfType<GDIBulldozer>()
            .FirstOrDefault(unit => unit.ArmyId == ai.Player.ArmyId && !unit.IsDying);
        if (bulldozer is null)
        {
            Goal = AIGoalState.FindingBulldozer;
            LastDecision = "No usable bulldozer found; I will try again.";
            return;
        }

        Goal = AIGoalState.FindingReactorSite;
        if (!TryFindReactorSite(world, ai, bulldozer, out Vector3 position))
        {
            LastDecision = "No valid reactor site found near the bulldozer; I will retry.";
            return;
        }

        Guid buildingId = Guid.NewGuid();
        network.EnqueueLocalMessage(NetworkCommands.CreateBuildRequest(
            ai.Id, "Reaktor", position.X, position.Y, position.Z, 0.0f, buildingId));
        network.EnqueueLocalMessage(NetworkCommands.CreateBuildConstructionRequest(
            ai.Id, [bulldozer.UnitId], buildingId));
        _reactorId = buildingId;
        _requestElapsed = 0.0f;
        Goal = AIGoalState.BuildRequested;
        ai.SetStatus(AIPlayerStatus.Building);
        LastDecision = $"Requested reactor at ({position.X:0.0}, {position.Z:0.0}).";
    }

    private static bool TryFindReactorSite(GameWorld world, AIPlayer ai, GDIBulldozer bulldozer, out Vector3 position)
    {
        var preview = new Reaktor(Vector3.Zero, Guid.NewGuid(), BuildingFactory.GetPurchasePrice("Reaktor"));
        preview.SetCreatorPlayer(ai.Id);
        preview.SetArmy(ai.Player.ArmyId);
        Point center = world.GameGrid.ToCell(bulldozer.Position);

        foreach (Point cell in CandidateCells(center, 5, 15))
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
