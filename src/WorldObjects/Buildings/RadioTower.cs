using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class CommunicationsTower : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public CommunicationsTower(
        Vector3 position,
        Guid unitId
        ) : base(
            position,
            unitId)
    {
        SightRange = 50;
        TotalBuildingPointsNeeded = 500;
        HitPoints = MaxHitPoints = 500;

        SetMesh("antenna-1", deriveDimensions: true);
    }

    public override void Update(GameTime gameTime)
    {
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
                new(UnitActionType.Follow, "Sell", 6, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return actions;
    }

}
