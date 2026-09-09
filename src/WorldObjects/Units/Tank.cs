using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

public class Tank : MobileUnit
{
    // The turret angle is local to the hull.  Keeping it this way means that a
    // rotating hull does not automatically drag the turret around in world space.
    public float TurretRotation { get; private set; }
    public float ReverseSpeed { get; set; } = 1.4f;
    public float ReverseWithoutTurningDistance { get; set; } = 6.0f;
    public float TurretRotationSpeed { get; set; } = 0.01f;
    public float TurnInPlaceDotThreshold { get; set; } = 0.5f;
    private string? _lastMovementMode;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    public Tank(Vector3 position, Guid unitId, IMovementProfile? movementProfile = null)
        : base(position, length: 4, width: 2, height: 2.2f, unitId, movementProfile)
    {
        MoveSpeed = 3.0f;
        RotationSpeed = 1.0f;
        HeadingSnapAngle = 0.0f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);        
        //TurretRotation = WrapAngle(TurretRotation + TurretRotationSpeed);
        TurretRotation = WrapAngle(TurretRotation + TurretRotationSpeed);
        //UpdateTurret(gameTime);
    }

    protected override void MoveAlongPath(GameTime gameTime)
    {
        if (PlannedPath.Count == 0)
        {
            _lastMovementMode = null;
            return;
        }

        Point nextCell = PlannedPath[0];
        Vector3 target = new(nextCell.X + 0.5f, Position.Y, nextCell.Y + 0.5f);
        Vector3 toTarget = target - Position;
        toTarget.Y = 0.0f;

        float distanceToTarget = toTarget.Length();
        if (distanceToTarget <= WaypointArrivalRadius)
        {
            LogMovementMode("arrive", distanceToTarget, directionDot: 1.0f);
            CompleteWaypoint();
            return;
        }

        Vector3 desiredDirection = toTarget / distanceToTarget;
        Vector3 forward = GetHorizontalDirection(Vector3.Forward);
        float directionDot = Vector3.Dot(forward, desiredDirection);
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // A nearby waypoint directly behind the tank is most naturally reached
        // in reverse.  Do not rotate the hull for this small correction.
        bool canReverse = directionDot < -0.35f &&
            distanceToTarget <= ReverseWithoutTurningDistance;
        if (canReverse)
        {
            LogMovementMode("reverse", distanceToTarget, directionDot);
            TryMoveTo(Position - forward * ReverseSpeed * deltaTime);
            return;
        }

        // Start driving only once the hull is reasonably aligned.  The former
        // threshold allowed nearly sideways movement, which let the tank orbit
        // its first waypoint instead of converging on it.
        bool needsTurnInPlace = directionDot < TurnInPlaceDotThreshold;
        forward = TurnTowards(desiredDirection, gameTime);
        if (needsTurnInPlace)
        {
            LogMovementMode("turn-in-place", distanceToTarget, directionDot);
            return;
        }

        // TurnTowards only applies RotationSpeed * deltaTime, so both the body
        // rotation and the resulting forward movement stay continuous.
        float alignment = MathF.Max(0.0f, Vector3.Dot(forward, desiredDirection));
        float speed = MoveSpeed * MathHelper.Lerp(0.45f, 1.0f, alignment);
        LogMovementMode("forward", distanceToTarget, alignment);
        TryMoveTo(Position + forward * speed * deltaTime);
    }

    private void UpdateTurret(GameTime gameTime)
    {
        Vector3 desiredDirection = GetHorizontalDirection(Vector3.Forward);

        if (AttackTargetId is Guid targetId &&
            Globals.World.Units.FindById(targetId) is Unit targetUnit)
        {
            desiredDirection = targetUnit.Position - Position;
        }
        else if (AttackGroundTarget is Vector3 groundTarget)
        {
            desiredDirection = groundTarget - Position;
        }

        desiredDirection.Y = 0.0f;
        if (desiredDirection.LengthSquared() <= 0.0001f)
            return;
        desiredDirection.Normalize();

        float hullYaw = DirectionToYaw(GetHorizontalDirection(Vector3.Forward));
        float desiredYaw = DirectionToYaw(desiredDirection);
        float currentTurretWorldYaw = hullYaw + TurretRotation;
        float maximumTurn = TurretRotationSpeed *
            (float)gameTime.ElapsedGameTime.TotalSeconds;
        float newTurretWorldYaw = currentTurretWorldYaw + MathHelper.Clamp(
            WrapAngle(desiredYaw - currentTurretWorldYaw),
            -maximumTurn,
            maximumTurn);

        TurretRotation = WrapAngle(newTurretWorldYaw - hullYaw);
    }

    private static float DirectionToYaw(Vector3 direction) =>
        MathF.Atan2(-direction.X, -direction.Z);

    private static float WrapAngle(float angle)
    {
        while (angle > MathHelper.Pi)
            angle -= MathHelper.TwoPi;
        while (angle < -MathHelper.Pi)
            angle += MathHelper.TwoPi;
        return angle;
    }

    private void LogMovementMode(string mode, float distance, float directionDot)
    {
        if (_lastMovementMode == mode)
            return;

        _lastMovementMode = mode;
        PathDebug($"tank mode={mode} distance={distance:0.00} alignment={directionDot:0.00}");
    }

    public override void Draw(Effect effect)
    {
        Globals.MeshHandler.DrawMesh(effect, Globals.MeshHandler.Meshes["tank"], GetWorldMatrix());
    }
}
