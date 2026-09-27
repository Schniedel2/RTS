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
        TotalBuildingPointsNeeded = 2500;
        HitPoints = MaxHitPoints = 2500;
        PowerConsumption = 0;

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
            "gunner" => 4.0f,
            "rak-zero" => 5.0f,
            //"flamer" => 6.0f,
            //"invasor" => 8.0f,
            "engineer" => 5.0f,
            "medic" => 5.0f,
            "squad-leader" => 6.0f,
            _ => 0.0f
        };
        return durationSeconds > 0.0f;
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = Array.Empty<UnitAction>();
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.TrainUnit, "Gunner", 6, 1, "gunner"),
                new(UnitActionType.TrainUnit, "Rak Zero", 6, 1, "rak-zero"),
                new(UnitActionType.TrainUnit, "Engineer", 6, 1, "engineer"),
                new(UnitActionType.TrainUnit, "Medic", 5, 1, "medic"),
                new(UnitActionType.TrainUnit, "Squad Leader", 4, 1, "squad-leader"),
                new(UnitActionType.SetRallyPoint, "Set rally point", 0, 1, RequiresTarget: true),
                new(UnitActionType.ClearRallyPoint, "Clear rally point", 7, 1, RequiresTarget: false),
                new(UnitActionType.Stop, "Cancel", 0, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
            ];
        }
        
        return WithSellAction(actions);
    }
}
