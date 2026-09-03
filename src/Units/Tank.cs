using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class Tank : Unit
{
    public Tank(
        Vector3 position,
        Guid unitId,
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            length: 5,
            width: 3,
            height: 2.2f,
            unitId,
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