using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBarracks : Building
{
    public override bool SupportsRallyPoint => true;

    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public GDIBarracks(
        Vector3 position,
        Guid unitId,
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        Length = 2;
        Width = 4;
        Height = 4;

        TotalBuildingPointsNeeded = 2500;
        HitPoints = MaxHitPoints = 2500;
        PowerConsumption = 20;

        SetMesh("barracks-1", deriveDimensions: true);
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public override bool TryGetProductionDuration(string unitTypeId, out float durationSeconds)
    {
        durationSeconds = unitTypeId.ToLowerInvariant() switch
        {
            "rak-zero" => 4.0f,
            "grunt" => 5.0f,
            "flamer" => 6.0f,
            "invasor" => 8.0f,
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
                new(UnitActionType.TrainUnit, "Rak Zero", 6, 1, "rak-zero"),
                new(UnitActionType.TrainUnit, "Grunt", 6, 1, "grunt"),
                new(UnitActionType.TrainUnit, "Flamer", 6, 1, "flamer"),
                new(UnitActionType.TrainUnit, "Invasor", 6, 1, "invasor"),
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
