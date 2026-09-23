using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class VehicleFactory : Building
{
    public override bool SupportsRallyPoint => true;

    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public VehicleFactory(
        Vector3 position,
        Guid unitId,
        string meshName,
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        TotalBuildingPointsNeeded = 2500;
        HitPoints = MaxHitPoints = 2500;

        SetMesh(meshName, deriveDimensions: true);
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public override bool TryGetProductionDuration(string unitTypeId, out float durationSeconds)
    {
        durationSeconds = unitTypeId.ToLowerInvariant() switch
        {
            "tank" => 10.0f,
            _ => 0.0f
        };
        return durationSeconds > 0.0f;
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions =
        [
            new(UnitActionType.SetRallyPoint, "Set rally point", 0, 1),
            new(UnitActionType.ClearRallyPoint, "Clear rally point", 7, 1),
            new(UnitActionType.Goto, "Cancel", 0, 1)
        ];
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.TrainUnit, "Tank", 6, 1, "tank"),
                new(UnitActionType.SetRallyPoint, "Set rally point", 0, 1),
                new(UnitActionType.ClearRallyPoint, "Clear rally point", 7, 1),
                new(UnitActionType.Goto, "Cancel", 0, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return WithSellAction(actions);
    }
}
