using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
        
    private bool _isManeuvering;

    public float ReverseSpeed { get; set; } = 2.0f;
    public float ReplanAngle { get; set; } = MathHelper.ToRadians(10.0f);

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
    }

    public override bool TryReceiveGotoCommand(GameWorld map, GotoCommand command, bool appendToQueue = false, IReadOnlyList<Point>? route = null)
    {
        bool accepted = base.TryReceiveGotoCommand(map, command, appendToQueue, route);
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

        // Align with the first path segment, not with the final command target.
        // The latter may lie behind a building which the calculated route is
        // deliberately leading around. Reversing towards that final target can
        // otherwise wedge a vehicle into the building before it starts its path.
        Vector2 target = CurrentCommand.Value.Target;
        if (PlannedPath.Count > 0)
        {
            Vector3 waypoint = Globals.World.GameGrid.ToWorldPosition(PlannedPath[0], Position.Y);
            target = new Vector2(waypoint.X, waypoint.Z);
        }
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
        // Reversing is only a visual maneuvering aid. If the space behind the
        // vehicle is occupied, keep rotating instead of repeatedly driving
        // into the blocker. TurnTowards above still advances the heading.
        _ = TryMoveTo(Position - forward * movementDistance);
    }
}
