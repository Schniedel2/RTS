using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Soldier : MobileUnit
{
    private readonly AnimationPlayer _animationPlayer;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    private float _nextIdlePoseTimeer = 0.0f;
    private bool _isMoving = false;
    private bool _isAiming = false;

    public Soldier(
        Vector3 position,
        Guid unitId,
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            length: 1,
            width: 1,
            height: 1.8f,
            unitId,
            movementProfile)            
    {
        MoveSpeed = 2.0f;
        RotationSpeed = MathHelper.TwoPi;
        // Infantry has no independently rotating turret. While attacking, the
        // body itself tracks the target at the same speed as normal turning.
        RotateBodyTowardsTarget = true;
        TargetAngleDegreesPerSecond = MathHelper.ToDegrees(RotationSpeed);

        //SetMesh("Soldier-1", deriveDimensions: true);
        SetMesh("Soldier-2", deriveDimensions: true);

        //turret mount point for the weapon
        int weapon = Random.Shared.Next(7);
        weapon = 0;
        if (weapon == 0)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["ak47"]);
        if (weapon == 1)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["breda-m1935pg"]);
        if (weapon == 2)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["brok17"]);
        if (weapon == 3)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["kraber-ap-sniper"]);
        if (weapon == 4)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["m16"]);
        if (weapon == 5)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["uzi-mac-10"]);
        if (weapon == 6)
            _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["minigun"]);
        
        this.AttackCooldown = 0.2f;

        _animationPlayer = new AnimationPlayer(_meshSet!.RootMesh.Animations);        
        _animationPlayer.Play("idle");
        _animationPlayer.SetRandomAnimationTime();
        _animationPlayer.Speed = 0.9f + Random.Shared.NextSingle() * 0.2f;
        //_animationPlayer.AddOverlay("pose:idleRifle", weight: 1.0f);        

        _animationPlayer.AddOverlay("arms:idleRifle0");
        _animationPlayer.AddOverlay("head:idle0");
        SetRandomArmsPose();
        SetRandomHeadPose();
    }

    public void SetRandomArmsPose()
    {
        int i = Random.Shared.Next(4);
        _animationPlayer.AddOverlayTransition("arms", "arms:idleRifle" + i, 0.4f + Random.Shared.NextSingle() * 0.2f);
    }
    public void SetRandomHeadPose()
    {
        int i = Random.Shared.Next(4);
        _animationPlayer.AddOverlayTransition("head", "head:idle" + i, 0.3f + Random.Shared.NextSingle() * 0.2f);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        bool wasAiming = _isAiming;
        bool wasMoving = _isMoving;

        _isMoving = PlannedPath.Count > 0;

        bool hasAttackOrder =
            AttackTargetId is not null ||
            AttackGroundTarget is not null;
        _isAiming = hasAttackOrder && !_isMoving;

        if (wasAiming && !_isAiming)
        {
            SetRandomArmsPose();
            SetRandomHeadPose();
        }

        if (!wasAiming && _isAiming)
        {
            _animationPlayer.RemoveOverlayLayer("arms");
            _animationPlayer.RemoveOverlayLayer("head");
        }

        if (_isMoving && !wasMoving)
            _animationPlayer.AddOverlayTransition("arms", "arms:idleRifle1", 0.5f);

        string anim = "idle";
        if (_isAiming)
            anim = "fire:rifle0";
        if (_isMoving)
            anim = "run";

        if (anim == "idle") 
        {
            _nextIdlePoseTimeer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_nextIdlePoseTimeer <= 0.0f)
            {
                if (Random.Shared.Next(10) <= 3)                
                    SetRandomArmsPose();
                if (Random.Shared.Next(10) <= 6)
                    SetRandomHeadPose();

                _nextIdlePoseTimeer = 3.0f + Random.Shared.NextSingle() * 5.0f;
            }
        }

        _animationPlayer.Play(anim);
        //_animationPlayer.AddOverlay("pose:idleRifle", weight: 1.0f);
        _animationPlayer.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public override void PlayShotEffects()
    {
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

        Globals.World.Particles.EmitRifleMuzzleFlash(muzzlePosition, barrelDirection);
    }

    public override void Draw(Effect effect)
    {
        _meshSet?.Draw(effect, GetVisualWorldMatrix(), GetMeshAnimationPose());
    }

    protected override AnimationPose GetMeshAnimationPose() => _animationPlayer.EvaluatePose();
}
