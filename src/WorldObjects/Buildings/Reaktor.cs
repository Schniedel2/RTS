using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Reaktor : Building
{
    public override string GameplayTypeId => "reaktor";
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();
    public override int EffectivePowerProduction =>
        PowerProduction + (Occupancy?.Count(OccupantRole.Crew) ?? 0) *
        (GameplayCatalog.Find(PurchasableType.Building, GameplayTypeId)?
            .Building?.PowerProductionPerCrew ?? 0);
    private int subType = 1;

    public Reaktor(
        Vector3 position,
        Guid unitId,        
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        ApplyCatalogMetadata();

        subType = 2;
        if (subType == 1)
            SetMesh("reaktor-1", deriveDimensions: true);
        if (subType == 2)
            SetMesh("reaktor-2", deriveDimensions: true);
    }

    public override void Update(GameTime gameTime)
    {
        if (IsCompleted)
        {
            if (subType == 1)
                UpdateExhaust(gameTime, 0.1f, SmokeEmissionPresets.ReactorExhaust());
            if (subType == 2)   
                UpdateExhaust(gameTime, 0.1f, SmokeEmissionPresets.ReactorExhaustSmall());
        }
        base.Update(gameTime);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = Array.Empty<UnitAction>();
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.Goto, "Override", 0, 1),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Stop, "Stop", 7, 1),
            ];
        }
        
        return WithSellAction(actions);
    }

}
