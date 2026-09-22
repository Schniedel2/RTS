using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GenericBuilding : Building
{
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public GenericBuilding(
        Vector3 position,
        Guid unitId,
        string modelName
        ) : base(
            position,
            unitId)
    {
        if (Occupancy is not null) 
            Occupancy.EntryEnabled = true;
        TotalBuildingPointsNeeded = 0;
        HitPoints = MaxHitPoints = 500;

        SetMesh(modelName, deriveDimensions: true);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions =
        [
            new(UnitActionType.LeaveContainer, "Leave", 5, 1)
        ];

        return actions;
    }

}
