using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>High-level state exposed for AI debugging and future behaviour selection.</summary>
public enum AIPlayerStatus
{
    Idle,
    Active,
    Building,
    Gathering
}

/// <summary>
/// A host-owned player identity. Future decision logic belongs here; all actual
/// game actions must still travel through the normal host command pipeline.
/// </summary>
public sealed class AIPlayer
{
    public Player Player { get; }
    public AIPlayerStatus Status { get; private set; } = AIPlayerStatus.Idle;
    public AIController Controller { get; } = new();

    public Guid Id => Player.Id;
    public string Name => Player.Name;

    public AIPlayer(Player player)
    {
        Player = player;
    }

    public void SetStatus(AIPlayerStatus status) => Status = status;

    public void BeginMatch(int matchSeed, Guid armyId)
    {
        Status = AIPlayerStatus.Active;
        Controller.BeginMatch(matchSeed, armyId);
    }

    public void Update(GameTime gameTime, GameWorld world, Network.NetworkHandler network)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.Player");
        long started = Stopwatch.GetTimestamp();
        Controller.Update(gameTime, this, world, network);
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Globals.Telemetry.AIUpdate_Calls++;
        Globals.Telemetry.AIUpdate_Last = elapsed;
        Globals.Telemetry.AIUpdate_Max = Math.Max(Globals.Telemetry.AIUpdate_Max, elapsed);
    }
}
