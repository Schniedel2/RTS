using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Gepard : MobileUnit
{
    public override string GameplayTypeId => "gepard";
    private float _pitchDegrees;
    private float _visualYawDegrees;
    private int _nextMuzzle;
    public override ArmorClass Armor => ArmorClass.HeavyVehicle;
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

    public Gepard(Vector3 position, Guid unitId, IMovementProfile? movementProfile = null)
        : base(position, unitId, movementProfile)
    {
        Occupancy = new OccupancyComponent(
            this,
            [new OccupantSlot(OccupantRole.Driver, 1, 1)],
            OccupancyOwnershipMode.ControllerDefinesOwnership,
            OccupantRole.Driver,
            becomeNeutralWithoutController: true);
            
        TargetAngleMinimumDegrees = -180.0f;
        TargetAngleMaximumDegrees = 180.0f;
        Behavior = UnitBehavior.Passive;
        MoveSpeed = 4.0f;
        RotationSpeed = 3.0f;
        SightRange = 24;
        HeadingSnapAngle = 0.0f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
        GroundSteering = new(
            MovingTurnDegreesPerSecond: MathHelper.ToDegrees(RotationSpeed),
            StationaryTurnDegreesPerSecond: MathHelper.ToDegrees(RotationSpeed),
            TurnInPlaceThresholdDegrees: 135.0f,
            MinimumCurveSpeedFactor: 0.45f,
            AllowReverse: true,
            ReverseSpeed: 1.4f,
            ReverseStartAngleDegrees: 110.0f,
            ReverseAlignmentToleranceDegrees: 5.0f,
            ReverseMaximumDistance: 6.0f);
        TargetAngleDegreesPerSecond = 50.0f;
        HitPoints = MaxHitPoints = 500;
        AttackRange = 32.0f;
        AttackDamage = 30.0f;
        AttackCooldown = 0.55f;
        AttackDamageType = DamageType.AntiTank;
        AllowedTargetDomains = TargetDomain.Air;

        SetMesh("gepard-1", deriveDimensions: true);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        float elapsed = Math.Max(0.0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _visualYawDegrees = MathHelper.ToDegrees(MathHelper.WrapAngle(
            MathHelper.ToRadians(TargetAngleDegrees)));
        float targetPitch = TryGetTargetPosition(out Vector3 target)
            ? GetPitchToTarget(target)
            : 0.0f;
        float pitchStep = 90.0f * elapsed;
        _pitchDegrees += MathHelper.Clamp(targetPitch - _pitchDegrees, -pitchStep, pitchStep);
        ApplyPivotRotations();
    }

    public override void PlayShotEffects()
    {
        Matrix aim = Matrix.CreateRotationX(MathHelper.ToRadians(_pitchDegrees)) *
            Matrix.CreateRotationY(MathHelper.ToRadians(_visualYawDegrees));
        Vector3 barrelDirection = Vector3.TransformNormal(Vector3.Forward, aim * Transform);
        barrelDirection = barrelDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(barrelDirection)
            : Vector3.Forward;

        // Alternate the two authored gun muzzles. Keep a hull-relative fallback
        // so a missing pivot does not suppress an otherwise valid attack.
        string muzzlePivot = _nextMuzzle == 0 ? "pivot:muzzle" : "pivot:muzzle-2";
        if (!TryGetMuzzleWorldPosition(muzzlePivot, out Vector3 muzzlePosition))
        {
            if (!TryGetMuzzleWorldPosition(out muzzlePosition))
                muzzlePosition = Position + Vector3.Up * (Height * 0.75f) +
                    barrelDirection * (Length * 0.52f);
        }

        Globals.World.Particles.EmitTurretMuzzleFlash(muzzlePosition, barrelDirection);
        _nextMuzzle = 1 - _nextMuzzle;
    }

    public override bool IsReadyToShoot(double hostTime) =>
        base.IsReadyToShoot(hostTime) && MathF.Abs(GetPitchToTargetForCurrentTarget() - _pitchDegrees) <= 2.0f;

    public override void Draw(Effect effect)
    {
        if (_meshSet is null)
            return;

        ApplyPivotRotations();
        _meshSet.Draw(effect, GetVisualWorldMatrix());
    }

    private void ApplyPivotRotations()
    {
        Quaternion yawRotation =
            Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(_visualYawDegrees));
        _meshSet?.SetPivotRotation("pivot:turret-yaw", yawRotation);
        _meshSet?.SetPivotRotation("pivot:turret-pitch",
            Quaternion.CreateFromAxisAngle(Vector3.Right, MathHelper.ToRadians(_pitchDegrees)));
    }

    private float GetPitchToTargetForCurrentTarget() =>
        TryGetTargetPosition(out Vector3 target) ? GetPitchToTarget(target) : 0.0f;

    private float GetPitchToTarget(Vector3 target)
    {
        float targetHeight = 0.5f;
        Guid? targetId = AttackTargetId ?? TemporaryTargetUnitId;
        if (targetId is Guid id && Globals.World.Units.FindById(id) is Unit targetUnit)
            targetHeight = targetUnit.Height * 0.5f;

        Vector3 delta = target + Vector3.Up * targetHeight -
            (Position + Vector3.Up * (Height * 0.7f));
        float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        return MathHelper.ToDegrees(MathF.Atan2(delta.Y, Math.Max(0.001f, horizontal)));
    }

}
