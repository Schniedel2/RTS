using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class Helipad : Building
{
    public bool DeliveryPending { get; internal set; }
    public bool IsReservedForDelivery => DeliveryPending || ProductionQueue.ActiveOrder is not null;
    public bool CanOrderHelicopter(GameWorld world) => IsCompleted && !IsDying &&
        !IsReservedForDelivery && !world.Units.Units.OfType<Helicopter>().Any(h =>
            !h.IsDying && h.AssignedHelipadId == UnitId);

    public override bool TryGetProductionDuration(string unitTypeId, out float durationSeconds)
    {
        durationSeconds = string.Equals(unitTypeId, "helicopter", StringComparison.OrdinalIgnoreCase) ? 10f : 0f;
        return durationSeconds > 0;
    }

    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();
    /// <summary>Center/top of this model's landing slab; optional pivot:landing overrides it.</summary>
    public Vector3 LandingLocalPosition { get; set; } = new(2.4f, 0.1f, -0.8f);

    public bool CanAccept(GameWorld world, Helicopter helicopter) => IsCompleted && !IsDying && !IsReservedForDelivery &&
        helicopter.IsAlly(this) && !world.Units.Units.OfType<Helicopter>().Any(other =>
            other != helicopter && !other.IsDying && other.AssignedHelipadId == UnitId);

    public Vector3 GetLandingSurfacePosition()
    {
        return TryGetAnimatedPivotWorldTransform("pivot:landing", out Matrix pivot)
            ? pivot.Translation : Vector3.Transform(LandingLocalPosition, GetWorldMatrix());
    }

    public Vector3 GetLandingPosition(Helicopter helicopter) =>
        GetLandingSurfacePosition() + Vector3.Up * helicopter.GroundOffset;

    public Helipad(
        Vector3 position,
        Guid unitId,
        string meshName = "helipad-1",
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        SetMesh(meshName, deriveDimensions: true);

        ProductionQueue.Capacity = 1;
        TotalBuildingPointsNeeded = 1500;
        HitPoints = MaxHitPoints = 1500;
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
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
                new(UnitActionType.TrainUnit, "Buy helicopter (0 resources, 10s)", 6, 1, "helicopter"),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Stop, "Destroy", 7, 1)
            ];
        }
        
        return WithSellAction(actions);
    }

}
