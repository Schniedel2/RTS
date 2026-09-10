using System;
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

    public Guid Id => Player.Id;
    public string Name => Player.Name;

    public AIPlayer(Player player)
    {
        Player = player;
    }

    public void SetStatus(AIPlayerStatus status) => Status = status;
}
