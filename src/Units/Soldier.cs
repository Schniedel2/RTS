using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class Soldier : Unit
{
    public Soldier(
        GraphicsDevice graphicsDevice,
        Vector3 position,
        IMovementProfile movementProfile = null) : base(
            graphicsDevice,
            position,
            length: 1,
            width: 1,
            height: 1.8f,
            movementProfile)
    {
        MoveSpeed = 2.0f;
        RotationSpeed = MathHelper.TwoPi;
    }
}