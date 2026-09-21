using Microsoft.Xna.Framework;

namespace RTS;

public interface IMovementProfile
{
    // Custom profiles can override this for additional terrain capabilities.
    bool CanUseTerrain(GridCell cell) => !cell.IsBlocked;

    bool CanEnter(GameWorld map, MobileUnit unit, Point cell);

    // A* uses geometric distance as its lower bound: costs must be at least
    // 1 for cardinal steps and sqrt(2) for diagonal steps.
    float GetMovementCost(
        GameWorld map,
        MobileUnit unit,
        Point from,
        Point to);
}