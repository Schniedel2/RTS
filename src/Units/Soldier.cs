using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class Soldier : Unit
{
    public Soldier(
        Vector3 position,
        Guid unitId,
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            length: 1,
            width: 1,
            height: 1.8f,
            unitId,
            movementProfile)
    {
        MoveSpeed = 2.0f;
        RotationSpeed = MathHelper.TwoPi;
    }
}