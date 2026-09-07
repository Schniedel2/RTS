using Microsoft.Xna.Framework;

namespace RTS;

public interface IMovementProfile
{
    bool CanEnter(GameWorld map, MobileUnit unit, Point cell);

    float GetMovementCost(
        GameWorld map,
        MobileUnit unit,
        Point from,
        Point to);
}