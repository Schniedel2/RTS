using System;

namespace RTS;

[Flags]
public enum MovementModes
{
    None = 0,
    Walk = 1,
    Drive = 2,
    Climb = 4,
    SteepDrive = 8,
    All = Walk | Drive | Climb | SteepDrive
}

/// <summary>Terrain rules, independent of the current occupant.</summary>
public sealed class GridCell
{
    public bool IsBlocked { get; set; }
    public bool ExcludeFromPathfinding { get; set; }
    public MovementModes AllowedMovement { get; set; } = MovementModes.All;
    public float MaxSlopeDegrees { get; internal set; }
    internal long TerrainRevision { get; set; } = -1;
    internal bool HasTerrain { get; set; } = true;
    private float _movementCost = 1.0f;

    /// <summary>At least 1, preserving the lower bound used by the A* heuristic.</summary>
    public float MovementCost
    {
        get => _movementCost;
        set
        {
            if (!float.IsFinite(value) || value < 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value), "Movement cost must be finite and at least 1.");
            _movementCost = value;
        }
    }
}
