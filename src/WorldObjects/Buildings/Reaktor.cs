using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Reaktor : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public Reaktor(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            unitId)
    {
        Length = 4;
        Width = 5;
        Height = 4;

        TotalBuildingPointsNeeded = 500;
        HitPoints = 500;

        SetMesh("reaktor", deriveDimensions: true);
    }

    public override void Update(GameTime gameTime)
    {
        if (IsCompleted)
        {
            UpdateExhaust(gameTime, 0.1f, SmokeEmissionPresets.ReactorExhaust());
        }
        base.Update(gameTime);
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
                new(UnitActionType.Goto, "Override", 0, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Follow, "Sell", 6, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return actions;
    }

}
