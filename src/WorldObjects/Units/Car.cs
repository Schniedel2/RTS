using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Car : MobileUnit
{
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.LeaveContainer, "Leave", 5, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];
        
    private bool _isManeuvering;

    public float ReverseSpeed { get; set; } = 2.0f;
    public float ReplanAngle { get; set; } = MathHelper.ToRadians(45.0f);

    public Car(
        Vector3 position,
        Guid unitId,        
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            length: 4,
            width: 2,
            height: 1.5f,
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
    }

    public override bool TryReceiveGotoCommand(GameWorld map, GotoCommand command)
    {
        bool accepted = base.TryReceiveGotoCommand(map, command);
        _isManeuvering = accepted && CanOnlyMoveForward && !CanTurnInPlace;

        return accepted;
    }

    protected override void MoveAlongPath(GameTime gameTime)
    {
        if (!_isManeuvering)
        {
            base.MoveAlongPath(gameTime);
            return;
        }

        if (!CurrentCommand.HasValue)
        {
            _isManeuvering = false;
            return;
        }

        Vector2 target = CurrentCommand.Value.Target;
        Vector3 desiredDirection = new(
            target.X - Position.X,
            0.0f,
            target.Y - Position.Z);

        if (desiredDirection == Vector3.Zero)
        {
            _isManeuvering = false;
            return;
        }

        desiredDirection.Normalize();
        Vector3 forward = TurnTowards(desiredDirection, gameTime);
        float directionDot = Vector3.Dot(forward, desiredDirection);

        if (directionDot >= MathF.Cos(ReplanAngle))
        {
            _isManeuvering = false;

            //if (!TryReplanPath(map))
            //    ClearCommand();

            return;
        }

        float movementDistance = ReverseSpeed *
            (float)gameTime.ElapsedGameTime.TotalSeconds;
        TryMoveTo(Position - forward * movementDistance);
    }
}
