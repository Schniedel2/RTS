using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

public class Tank : MobileUnit
{
    public override bool UsesVehicleDeathSequence => true;
    // The turret angle is local to the hull.  Keeping it this way means that a
    // rotating hull does not automatically drag the turret around in world space.
    public float ReverseSpeed { get; set; } = 1.4f;
    public float ReverseWithoutTurningDistance { get; set; } = 6.0f;
    public float TurnInPlaceDotThreshold { get; set; } = 0.995f;
    /// <summary>Current local barrel displacement, used for visual recoil.</summary>
    public Vector3 BarrelRecoilOffset { get; private set; }
    public Vector3 BarrelRecoilOnShot { get; set; } = new(0.0f, 0.0f, 0.3f);
    /// <summary>Fraction of remaining barrel recoil recovered per nominal 60 FPS frame.</summary>
    public float BarrelRecoilRecoveryFactor { get; set; } = 0.05f;
    public SmokeEmissionSettings CannonSmokeSettings { get; set; } = SmokeEmissionPresets.TankCannon();
    public MuzzleFlashEmissionSettings CannonMuzzleFlashSettings { get; set; } =
        MuzzleFlashEmissionPresets.TankCannon();
    private string? _lastMovementMode;

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

    public Tank(Vector3 position, Guid unitId, IMovementProfile? movementProfile = null)
        : base(position, length: 4, width: 2, height: 2.2f, unitId, movementProfile)
    {
        Occupancy = new OccupancyComponent(
            this,
            [new OccupantSlot(OccupantRole.Driver, 1, 1)],
            OccupancyOwnershipMode.ControllerDefinesOwnership,
            OccupantRole.Driver,
            becomeNeutralWithoutController: true);
        Behavior = UnitBehavior.Passive;
        MoveSpeed = 3.0f;
        RotationSpeed = 1.0f;
        SightRange = 24;
        HeadingSnapAngle = 0.0f;
        CanOnlyMoveForward = true;
        CanTurnInPlace = true;
        TargetAngleDegreesPerSecond = 50.0f;
        VisualRecoilPivot = new Vector3(0.0f, 0.0f, -0.2f);
        HitPoints = MaxHitPoints = 500;

        SetMesh("Tank-1", deriveDimensions: true);
        /*
        _meshSet = new MeshSet(Globals.MeshHandler.Meshes["TankBody-1"]);
        _meshSet.SetAttachment("pivot:turret", Globals.MeshHandler.Meshes["TankTurret-1"]);
        _meshSet.SetAttachmentPath("pivot:turret/pivot:barrel", Globals.MeshHandler.Meshes["TankBarrel-2"]);
        */
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

        // Reverse towards nearby rear waypoints, steering the rear towards
        // the target as well. Straight reversing cannot reach offset targets.
        bool canReverse = directionDot < -0.35f &&
            distanceToTarget <= ReverseWithoutTurningDistance;
        if (canReverse)
        {
            forward = TurnTowards(-desiredDirection, gameTime);
            if (Vector3.Dot(-forward, desiredDirection) < TurnInPlaceDotThreshold)
                return;
            LogMovementMode("reverse", distanceToTarget, directionDot);
            TryMoveTo(Position - forward * Math.Min(distanceToTarget, ReverseSpeed * deltaTime));
            return;
        }

        // Start driving only once the hull is reasonably aligned.  The former
        // threshold allowed nearly sideways movement, which let the tank orbit
        // its first waypoint instead of converging on it.
        forward = TurnTowards(desiredDirection, gameTime);
        if (Vector3.Dot(forward, desiredDirection) < TurnInPlaceDotThreshold)
        {
            LogMovementMode("turn-in-place", distanceToTarget, directionDot);
            return;
        }

        // TurnTowards only applies RotationSpeed * deltaTime, so both the body
        // rotation and the resulting forward movement stay continuous.
        float alignment = MathF.Max(0.0f, Vector3.Dot(forward, desiredDirection));
        float speed = MoveSpeed * MathHelper.Lerp(0.45f, 1.0f, alignment);
        LogMovementMode("forward", distanceToTarget, alignment);
        TryMoveTo(Position + forward * Math.Min(distanceToTarget, speed * deltaTime));
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        float recovery = 1.0f - MathF.Pow(
            1.0f - Math.Clamp(BarrelRecoilRecoveryFactor, 0.0f, 1.0f),
            (float)gameTime.ElapsedGameTime.TotalSeconds * 60.0f);
        BarrelRecoilOffset = Vector3.Lerp(BarrelRecoilOffset, Vector3.Zero, recovery);
    }

    public override void PlayShotEffects()
    {
        BarrelRecoilOffset = BarrelRecoilOnShot;
        TriggerVisualRecoil(new Vector3(0.0f, 0.0f, 0.0f), -5.0f);
        Vector3 localBarrelDirection = Vector3.TransformNormal(
            Vector3.Forward,
            Matrix.CreateRotationY(MathHelper.ToRadians(TargetAngleDegrees)));
        Vector3 barrelDirection = Vector3.TransformNormal(localBarrelDirection, Transform);
        barrelDirection = barrelDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(barrelDirection)
            : Vector3.Forward;

        // A projectile already has this kind of fallback in NetworkInput.  Do
        // the same for the local smoke effect: a temporarily missing or
        // renamed pivot must not make a perfectly valid host shot look silent.
        // The fallback is close to the front of the hull until the BBModel
        // contains a usable "pivot:muzzle" again.
        if (!TryGetMuzzleWorldPosition(out Vector3 muzzlePosition))
            muzzlePosition = Position + Vector3.Up * (Height * 0.75f) +
                barrelDirection * (Length * 0.52f);

        // Keep the independently configurable smoke preset while using the
        // cannon flash preset for size, lifetime, color and flame spread.
        Globals.World.Particles.EmitCannonMuzzleFlash(
            muzzlePosition,
            barrelDirection,
            CannonMuzzleFlashSettings with { SmokeSettings = CannonSmokeSettings });
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
        if (_meshSet is null)
            return;

        _meshSet.SetParameter(Mesh.TurretAngle, MathHelper.ToRadians(TargetAngleDegrees));

        // Rotate only the authored recoil group. In the new Tank BBModel the
        // tracks are outside this group and therefore remain planted. Recoil
        // follows the turret direction by rotating the local pitch axis.
        float aimAngleRadians = MathHelper.ToRadians(TargetAngleDegrees);
        Vector3 recoilPitchAxis = Vector3.TransformNormal(
            Vector3.Right,
            Matrix.CreateRotationY(aimAngleRadians));
        Quaternion bodyRecoilRotation = Quaternion.CreateFromAxisAngle(
            recoilPitchAxis,
            MathHelper.ToRadians(VisualRecoilPitchDegrees));
        bool usesAuthoredBodyRecoil = _meshSet.SetPivotRotation(
            "pivot:recoil",
            bodyRecoilRotation);

        // Prefer the authored node so an integrated barrel and everything
        // below it (muzzle/projectile pivots included) recoil together. Older
        // composed tank meshes keep using the attachment transform fallback.
        if (!_meshSet.SetPivotTranslation("pivot:barrelrecoil", BarrelRecoilOffset))
        {
            _meshSet.SetAttachmentLocalTransform(
                "pivot:turret/pivot:barrel",
                Matrix.CreateTranslation(BarrelRecoilOffset));
        }
        _meshSet.Draw(
            effect,
            usesAuthoredBodyRecoil && !IsDying ? GetWorldMatrix() : GetVisualWorldMatrix());
    }

}
