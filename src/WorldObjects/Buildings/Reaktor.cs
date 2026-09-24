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
        Guid unitId,
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        Length = 4;
        Width = 5;
        Height = 4;

        TotalBuildingPointsNeeded = 500;
        HitPoints = MaxHitPoints = 500;

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
                new(UnitActionType.Stop, "Stop", 7, 1),
                new(UnitActionType.Destroy, "Destroy", 7, 1)
            ];
        }
        
        return WithSellAction(actions);
    }

}
