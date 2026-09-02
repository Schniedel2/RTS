using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class Tank : Unit
{
    public Tank(
        GraphicsDevice graphicsDevice,
        Vector3 position,
        IMovementProfile? movementProfile = null,
        Guid? unitId = null) : base(
            graphicsDevice,
            position,
            length: 5,
            width: 3,
            height: 2.2f,
            movementProfile,
            unitId)
    {
        MoveSpeed = 3.0f;
        RotationSpeed = 1.5f;
        ForwardMovementDotThreshold = 0.999f;
        HeadingSnapAngle = MathHelper.PiOver4;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
    }
}