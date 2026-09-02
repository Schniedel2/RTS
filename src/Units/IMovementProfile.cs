using Microsoft.Xna.Framework;

namespace RTS;

public interface IMovementProfile
{
    bool CanEnter(GameWorld map, Unit unit, Point cell);

    float GetMovementCost(
        GameWorld map,
        Unit unit,
        Point from,
        Point to);
}