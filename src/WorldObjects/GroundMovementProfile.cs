using Microsoft.Xna.Framework;

namespace RTS;

public class GroundMovementProfile : IMovementProfile
{
    public bool CanEnter(GameWorld map, MobileUnit unit, Point cell)
    {
        return map.CanMove(cell.X, cell.Y, unit);
    }

    public float GetMovementCost(
        GameWorld map,
        MobileUnit unit,
        Point from,
        Point to)
    {
        bool movesDiagonally = from.X != to.X && from.Y != to.Y;

        return movesDiagonally ? 1.4142135f : 1.0f;
    }
}