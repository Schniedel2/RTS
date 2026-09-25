using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Turret : Building
{
    private const float InactivePitchDegrees = -45.0f;
    private const float MinimumPitchDegrees = -60.0f;
    private const float MaximumPitchDegrees = 45.0f;
    private readonly Random _idleRandom;
    private float _idleYawDegrees;
    private float _idlePitchDegrees;
    private float _idlePauseRemaining;
    private float _pitchDegrees;
    private float _visualYawDegrees;
    private float _minigunRotationDegrees;
    private float _minigunRotationSpeed;
    private float _barrelSpinRemaining;
    private int _nextMuzzle;

    public float MinigunRotationMaxSpeed { get; set; } = 720.0f;
    public float MinigunRotationAcceleration { get; set; } = 900.0f;
    public float PitchDegreesPerSecond { get; set; } = 90.0f;
    public override bool UsesHitscanWeapon => true;
    public override bool CanFireWeapon => IsOperational && HasEnoughPower;
    public bool HasEnoughPower => ArmyId is Guid armyId &&
        ArmyPowerStatus.Calculate(Globals.World.Units.Units, armyId).HasEnoughPower;
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public Turret(Vector3 position, Guid unitId, string meshName, int purchasePrice)
        : base(position, unitId, purchasePrice)
    {
        SetMesh(meshName, deriveDimensions: true);
        _idleRandom = new Random(unitId.GetHashCode());
        ChooseNextIdlePose();
        TotalBuildingPointsNeeded = 500;
        HitPoints = MaxHitPoints = 500;
        //PowerConsumption = 20;
        PowerConsumption = 0;
        AttackRange = 22.0f;
        AttackDamage = 7.0f;
        AttackCooldown = 0.09f;
        TargetAngleMinimumDegrees = -360.0f;
        TargetAngleMaximumDegrees = 360.0f;
        TargetAngleDegreesPerSecond = 120.0f;
        Behavior = UnitBehavior.Aggressive;
        SightRange = 24;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        bool active = CanFireWeapon;
        // An unpowered or manually disabled turret has no motor movement:
        // retain its last yaw while the weapon mount settles downward.
        float yaw = active ? TargetAngleDegrees : _visualYawDegrees;

        if (active && !HasCombatTarget)
        {
            bool atIdlePose =
                MathF.Abs(ShortestAngle(_visualYawDegrees, _idleYawDegrees)) <= 1.0f &&
                MathF.Abs(_pitchDegrees - _idlePitchDegrees) <= 1.0f;
            if (atIdlePose)
            {
                _idlePauseRemaining -= elapsed;
                if (_idlePauseRemaining <= 0.0f)
                    ChooseNextIdlePose();
            }
            yaw = ApproachAngle(_visualYawDegrees, _idleYawDegrees, TargetAngleDegreesPerSecond * elapsed);
        }
        _visualYawDegrees = MathHelper.ToDegrees(MathHelper.WrapAngle(MathHelper.ToRadians(yaw)));

        float desiredPitch = !active
            ? InactivePitchDegrees
            : HasCombatTarget
                ? GetTargetPitchDegrees()
                : _idlePitchDegrees;
        _pitchDegrees = MathHelper.Clamp(
            _pitchDegrees + MathHelper.Clamp(desiredPitch - _pitchDegrees,
                -PitchDegreesPerSecond * elapsed, PitchDegreesPerSecond * elapsed),
            MinimumPitchDegrees, MaximumPitchDegrees);

        _barrelSpinRemaining = Math.Max(0.0f, _barrelSpinRemaining - elapsed);
        bool spin = active && _barrelSpinRemaining > 0.0f;
        _minigunRotationSpeed = MathHelper.Clamp(
            _minigunRotationSpeed + (spin ? 1.0f : -1.0f) * MinigunRotationAcceleration * elapsed,
            0.0f, MinigunRotationMaxSpeed);
        _minigunRotationDegrees = MathHelper.ToDegrees(MathHelper.WrapAngle(MathHelper.ToRadians(
            _minigunRotationDegrees + _minigunRotationSpeed * elapsed)));
        ApplyPivotRotations();
    }

    private void ApplyPivotRotations()
    {
        Quaternion yawRotation =
            Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(_visualYawDegrees));
        // The first authored Gatling asset used pivot:turret. New assets use
        // the more explicit pivot:turret-yaw; keep both conventions working.
        _meshSet?.SetPivotRotation("pivot:turret-yaw", yawRotation);
        _meshSet?.SetPivotRotation("pivot:turret-pitch",
            Quaternion.CreateFromAxisAngle(Vector3.Right, MathHelper.ToRadians(_pitchDegrees)));
        Quaternion barrelRotation = Quaternion.CreateFromAxisAngle(
            Vector3.Forward, MathHelper.ToRadians(_minigunRotationDegrees));
        _meshSet?.SetPivotRotation("pivot:barrel", barrelRotation);
        _meshSet?.SetPivotRotation("pivot:barrel-2", barrelRotation);
    }

    private float GetTargetPitchDegrees()
    {
        if (!TryGetTargetPosition(out Vector3 target))
            return 0.0f;

        float targetHeight = 0.0f;
        Guid? targetId = AttackTargetId ?? TemporaryTargetUnitId;
        if (targetId is Guid id && Globals.World.Units.FindById(id) is Unit targetUnit)
            targetHeight = targetUnit.Height * 0.5f;

        Vector3 delta = target + Vector3.Up * targetHeight -
            (Position + Vector3.Up * (Height * 0.7f));
        float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        // This model's authored pitch axis uses negative angles for downward aim.
        return MathHelper.ToDegrees(MathF.Atan2(delta.Y, MathF.Max(0.001f, horizontal)));
    }

    public override bool IsReadyToShoot(double hostTime)
    {
        if (!base.IsReadyToShoot(hostTime))
            return false;
        return MathF.Abs(GetTargetPitchDegrees() - _pitchDegrees) <= 2.0f;
    }

    public override bool TryGetMuzzleWorldPosition(out Vector3 position) =>
        TryGetMuzzleWorldPosition(_nextMuzzle == 0 ? "pivot:muzzle" : "pivot:muzzle-2", out position) ||
        base.TryGetMuzzleWorldPosition(out position);

    public override void PlayShotEffects()
    {
        // PlayShotEffects is called only for a host-confirmed shot on every
        // peer. Keeping the barrels active briefly bridges the interval until
        // the next Gatling round without spinning merely because a target exists.
        _barrelSpinRemaining = Math.Max(_barrelSpinRemaining, AttackCooldown + 0.08f);
        if (!TryGetMuzzleWorldPosition(out Vector3 muzzle))
            muzzle = Position + Vector3.Up * (Height * 0.7f);
        Vector3 direction = Vector3.TransformNormal(Vector3.Forward,
            Matrix.CreateRotationX(MathHelper.ToRadians(_pitchDegrees)) *
            Matrix.CreateRotationY(MathHelper.ToRadians(_visualYawDegrees)) * Transform);
        direction = direction.LengthSquared() > 0.0001f ? Vector3.Normalize(direction) : Vector3.Forward;
        Globals.World.Particles.EmitTurretMuzzleFlash(muzzle, direction);
        _nextMuzzle = 1 - _nextMuzzle;
    }

    private void ChooseNextIdlePose()
    {
        _idleYawDegrees = MathHelper.Lerp(-180.0f, 180.0f, (float)_idleRandom.NextDouble());
        // Look mostly towards the ground, with an occasional slight upward scan.
        _idlePitchDegrees = MathHelper.Lerp(-25.0f, 12.0f, (float)_idleRandom.NextDouble());
        _idlePauseRemaining = MathHelper.Lerp(2.0f, 5.0f, (float)_idleRandom.NextDouble());
    }

    public override string GetDebugCommandText()
    {
        if (!IsCompleted)
            return base.GetDebugCommandText();
        if (!IsEnabled)
            return "Disabled";
        if (!HasEnoughPower)
            return "No power";
        return HasCombatTarget
            ? $"Aiming yaw={_visualYawDegrees:0.0} pitch={_pitchDegrees:0.0}"
            : "Idle scan";
    }

    private static float ApproachAngle(float current, float target, float maximumStep)
    {
        float delta = ShortestAngle(current, target);
        return current + MathHelper.Clamp(delta, -maximumStep, maximumStep);
    }

    private static float ShortestAngle(float current, float target) =>
        MathHelper.ToDegrees(MathHelper.WrapAngle(MathHelper.ToRadians(target - current)));

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = [new(UnitActionType.Goto, "Cancel", 0, 1)];
        if (IsCompleted)
        {
            actions =
            [
                new(UnitActionType.Attack, "Attack", 1, 1),
                new(UnitActionType.ToggleEnabled, IsEnabled ? "Disable" : "Enable", 5, 1,
                    RequiresTarget: false),
                new(UnitActionType.Stop, "Stop", 7, 1, RequiresTarget: false),
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
                new(UnitActionType.Destroy, "Destroy", 7, 1)
            ];
        }
        return WithSellAction(actions);
    }
}
