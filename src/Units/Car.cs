using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class Car : Unit
{
    private bool _isManeuvering;

    public float ReverseSpeed { get; set; } = 2.0f;
    public float ReplanAngle { get; set; } = MathHelper.ToRadians(45.0f);

    public Car(
        GraphicsDevice graphicsDevice,
        Vector3 position,
        IMovementProfile movementProfile = null) : base(
            graphicsDevice,
            position,
            length: 4,
            width: 2,
            height: 1.5f,
            movementProfile)
    {
        MoveSpeed = 7.0f;
        RotationSpeed = 4.0f;
        WaypointArrivalRadius = 1.5f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = false;
    }

        public override bool TryReceiveGotoCommand(GameWorld map, GotoCommand command)
        {
            bool accepted = base.TryReceiveGotoCommand(map, command);
            _isManeuvering = accepted;

            return accepted;
        }

        protected override void MoveAlongPath(
            GameTime gameTime,
            Terrain terrain,
            GameWorld map)
        {
            if (!_isManeuvering)
            {
                base.MoveAlongPath(gameTime, terrain, map);
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

                if (!TryReplanPath(map))
                    ClearCommand();

                return;
            }

            float movementDistance = ReverseSpeed *
                (float)gameTime.ElapsedGameTime.TotalSeconds;
            TryMoveTo(map, Position - forward * movementDistance);
        }
}