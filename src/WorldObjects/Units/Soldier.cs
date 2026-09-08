using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Soldier : MobileUnit
{
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

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