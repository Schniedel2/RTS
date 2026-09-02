using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class Tank : Unit
{
    public Tank(
        GraphicsDevice graphicsDevice,
        Vector3 position,
        IMovementProfile movementProfile = null) : base(
            graphicsDevice,
            position,
            length: 5,
            width: 3,
            height: 2.2f,
            movementProfile)
    {
        MoveSpeed = 3.0f;
        RotationSpeed = 1.5f;
        ForwardMovementDotThreshold = 0.999f;
        HeadingSnapAngle = MathHelper.PiOver4;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
    }
}