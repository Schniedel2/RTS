using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;

namespace RTS;

public class Car : MobileUnit
{
    public override ArmorClass Armor => ArmorClass.LightVehicle;
    public override bool UsesVehicleDeathSequence => true;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Scouting, "AI: Scouting", 5, 1),
        new(UnitActionType.MoveAway, "AI: Move away", 4, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.LeaveContainer, "Leave", 5, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];
        


    public Car(
        Vector3 position,
        Guid unitId,        
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            unitId,
            movementProfile)
    {
        Occupancy = new OccupancyComponent(
            this,
            [new OccupantSlot(OccupantRole.Driver, 1, 1)],
            OccupancyOwnershipMode.ControllerDefinesOwnership,
            OccupantRole.Driver,
            becomeNeutralWithoutController: true);
        Behavior = UnitBehavior.Passive;
        MoveSpeed = 7.0f;
        RotationSpeed = 4.0f;
        WaypointArrivalRadius = 1.5f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = false;
        GroundSteering = new(
            MovingTurnDegreesPerSecond: MathHelper.ToDegrees(RotationSpeed),
            StationaryTurnDegreesPerSecond: MathHelper.ToDegrees(RotationSpeed),
            TurnInPlaceThresholdDegrees: 180.0f,
            MinimumCurveSpeedFactor: 0.55f,
            AllowReverse: true,
            ReverseSpeed: 2.0f,
            ReverseStartAngleDegrees: 100.0f,
            ReverseAlignmentToleranceDegrees: 10.0f,
            ReverseMaximumDistance: 6.0f);
    }

}
