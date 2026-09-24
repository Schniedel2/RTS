using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Engineer : Soldier
{
    public Engineer(
        Vector3 position,
        Guid unitId        
        ) : base(
            position,
            unitId,
            new GroundMovementProfile(MovementModes.Walk, 50.0f))
    {
        SetMesh("Soldier-2", deriveDimensions: true);
        SetWeapon(Weapon.Toolkit);
    }    

    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Scouting, "AI: Scouting", 5, 1),
        new(UnitActionType.MoveAway, "AI: Move away", 4, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];
}