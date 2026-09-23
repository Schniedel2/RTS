using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class Silo : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public Silo(
        Vector3 position,
        Guid unitId,
        string meshName = "silo-1"
        ) : base(
            position,
            unitId)
    {
        SetMesh(meshName, deriveDimensions: true);

        TotalBuildingPointsNeeded = 1500;
        HitPoints = MaxHitPoints = 1500;
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions =
        [
            new(UnitActionType.Goto, "Cancel", 0, 1)
        ];
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return actions;
    }

}
