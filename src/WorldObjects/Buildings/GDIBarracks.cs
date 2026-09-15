using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBarracks : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public GDIBarracks(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            unitId)
    {
        Length = 2;
        Width = 4;
        Height = 4;

        TotalBuildingPointsNeeded = 2500;
        HitPoints = 2500;
    }

    public override void Draw(Effect effect)
    {
        Matrix world = GetWorldMatrix();
        Globals.MeshHandler.DrawMesh(effect, "gdi-barracks", world);
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
                new(UnitActionType.TrainUnit, "Rak Zero", 6, 1),
                new(UnitActionType.TrainUnit, "Grunt", 6, 1),
                new(UnitActionType.TrainUnit, "Flamer", 6, 1),
                new(UnitActionType.TrainUnit, "Invasor", 6, 1),
                new(UnitActionType.Goto, "Cancel", 0, 1),
                new(UnitActionType.Follow, "Sell", 6, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return actions;
    }
}