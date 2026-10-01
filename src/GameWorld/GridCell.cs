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
    private readonly Action? _changed;
    public GridCell(Action? changed = null) => _changed = changed;
    private bool _blocked, _excluded;
    private MovementModes _allowed = MovementModes.All;
    public bool IsBlocked { get => _blocked; set { if (_blocked == value) return; _blocked = value; _changed?.Invoke(); } }
    public bool ExcludeFromPathfinding { get => _excluded; set { if (_excluded == value) return; _excluded = value; _changed?.Invoke(); } }
    public MovementModes AllowedMovement { get => _allowed; set { if (_allowed == value) return; _allowed = value; _changed?.Invoke(); } }
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
            if (_movementCost == value) return;
            _movementCost = value;
            _changed?.Invoke();
        }
    }
}
