using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBulldozer : Car
{
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Build, "Build Base", 0, 1, "GDI-Base"),
        new(UnitActionType.Build, "Build Barracks", 0, 1, "GDI-Barracks"),
        new(UnitActionType.BuildConstruction, "Build construction site", 0, 1),
    ];

    public GDIBulldozer(
        Vector3 position,
        Guid unitId,        
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            unitId,
            movementProfile)
    {
        MoveSpeed = 7.0f;
        RotationSpeed = 4.0f;
        WaypointArrivalRadius = 1.5f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = false;

        Length = 4;
        Width = 3;
        Height = 1.5f;
    }
}
