using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class GDIBarracks : Building
{
    public override string GameplayTypeId => "gdi-barracks";
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
        ApplyCatalogMetadata();

        SetMesh("barracks-1", deriveDimensions: true);
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = Array.Empty<UnitAction>();
    
        if (IsCompleted)
        {
            actions =
            [
                .. GameplayCatalog.CreateProductionActions(GameplayTypeId),
                new(UnitActionType.SetRallyPoint, "Set rally point", 0, 1, RequiresTarget: true),
                new(UnitActionType.ClearRallyPoint, "Clear rally point", 7, 1, RequiresTarget: false),
                new(UnitActionType.Stop, "Cancel", 0, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
            ];
        }
        
        return WithSellAction(actions);
    }
}
