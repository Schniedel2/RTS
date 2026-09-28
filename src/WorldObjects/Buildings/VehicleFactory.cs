using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class VehicleFactory : Building
{
    public override string GameplayTypeId => "vehicle-factory";
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
        ApplyCatalogMetadata();

        SetMesh(meshName, deriveDimensions: true);
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions =
        [
            new(UnitActionType.SetRallyPoint, "Set rally point", 0, 1),
            new(UnitActionType.ClearRallyPoint, "Clear rally point", 7, 1),
        ];
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.SetRallyPoint, "Set rally point", 0, 1),
                .. GameplayCatalog.CreateProductionActions(GameplayTypeId),
                new(UnitActionType.ClearRallyPoint, "Clear rally point", 7, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
            ];
        }
        
        return WithSellAction(actions);
    }
}
