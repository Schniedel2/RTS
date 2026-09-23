using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class TiberiumRefinery : Building
{
    public override float ResourceCapacity => 5000.0f;
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public TiberiumRefinery(
        Vector3 position,
        Guid unitId,
        string meshName = "tiberium-refinery-1",
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        SetMesh(meshName, deriveDimensions: true);

        ProductionQueue.Capacity = 1;
        TotalBuildingPointsNeeded = 4500;
        HitPoints = MaxHitPoints = 2500;
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public override void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        base.Draw2D(spriteBatch, camera, viewport);
        if (IsCompleted)
            ResourceBarRenderer.Draw(spriteBatch, camera, viewport, this,
                StoredResources, ResourceCapacity, Color.LimeGreen);
    }

    public bool TryQueueIncludedHarvester(Guid ownerPlayerId)
    {
        if (!IsCompleted || IncludedUnitGranted || ProductionQueue.ActiveOrder is not null || ownerPlayerId == Guid.Empty)
            return false;
        if (!TryQueueProduction(Guid.NewGuid(), "harvester", ownerPlayerId, 0.1f))
            return false;
        IncludedUnitGranted = true;
        MarkStateDirty();
        return true;
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
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return WithSellAction(actions);
    }

}
