using Microsoft.Xna.Framework;
using System;

namespace RTS;

public class GroundMovementProfile : IMovementProfile
{
    public MovementModes Capabilities { get; }
    public float MaxSlopeDegrees { get; }
    public float MaxSpecialSlopeDegrees { get; }

    public GroundMovementProfile(
        MovementModes capabilities = MovementModes.Drive,
        float maxSlopeDegrees = 35.0f,
        float maxSpecialSlopeDegrees = 85.0f)
    {
        if (!float.IsFinite(maxSlopeDegrees) || maxSlopeDegrees < 0 || maxSlopeDegrees > 90 ||
            !float.IsFinite(maxSpecialSlopeDegrees) || maxSpecialSlopeDegrees < 0 || maxSpecialSlopeDegrees > 90)
            throw new ArgumentOutOfRangeException(nameof(maxSlopeDegrees));
        Capabilities = capabilities;
        MaxSlopeDegrees = maxSlopeDegrees;
        MaxSpecialSlopeDegrees = maxSpecialSlopeDegrees;
    }

    public bool CanUseTerrain(GridCell cell)
    {
        if (cell.IsBlocked)
            return false;
        MovementModes available = cell.AllowedMovement & Capabilities;
        return ((available & (MovementModes.Walk | MovementModes.Drive)) != 0 && cell.MaxSlopeDegrees <= MaxSlopeDegrees) ||
            ((available & (MovementModes.Climb | MovementModes.SteepDrive)) != 0 && cell.MaxSlopeDegrees <= MaxSpecialSlopeDegrees);
    }

    public bool CanEnter(GameWorld map, MobileUnit unit, Point cell) =>
        map.CanMove(cell.X, cell.Y, unit);

    public float GetMovementCost(GameWorld map, MobileUnit unit, Point from, Point to)
    {
        bool diagonal = from.X != to.X && from.Y != to.Y;
        return (diagonal ? 1.4142135f : 1.0f) * map.GameGrid.GetMovementCost(unit, to);
    }
}
