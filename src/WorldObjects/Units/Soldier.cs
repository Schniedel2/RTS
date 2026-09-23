using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Soldier : MobileUnit
{    
    public enum Weapon
    {
        //  pistol
        Brok17 = 0,
        UziMac10,
        //  assault rifle
        Ak47,
        BredaM1935PG,
        M16,
        AR15,
        StenMk2Apocalypse,
        //  sniper / hunting rifles        
        KraberAPSniper,
        HuntingRifle,
        //  minigun
        Minigun,
        //  launchers
        RPG
    }

    private static readonly Weapon[] AvailableWeapons = Enum.GetValues<Weapon>();

    private readonly AnimationPlayer _animationPlayer;
    public Weapon EquippedWeapon { get; private set; }
    public override bool UsesHitscanWeapon => EquippedWeapon != Weapon.RPG;
    public override ProjectileKind ProjectileKind => EquippedWeapon == Weapon.RPG
        ? ProjectileKind.Rocket
        : ProjectileKind.None;
    public override float ProjectileSpeed => EquippedWeapon == Weapon.RPG ? 14.0f : base.ProjectileSpeed;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 1),
        new(UnitActionType.Scouting, "AI: Scouting", 5, 1),
        new(UnitActionType.Attack, "Attack", 1, 1),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 7, 1)
    ];

    protected List<string> ArmsOverlayClips = new List<string>();
    protected List<string> FireClips = new List<string>();
    protected List<string> DeathClips = new List<string>();
    protected List<string> RunArmsOverlayClips = new List<string>();
    protected List<string> HeadOverlayClips = new List<string>();


    protected float _minigunRotationDegrees = 0.0f;
    protected float _minigunRotationSpeed = 0.0f;
    protected float _minigunRotationMaxSpeed = 256.0f;
    protected float _minigunRotationAcceleration = 64.0f;
    private float _nextIdlePoseTimer = 0.0f;
    private float _launcherReloadRemaining;
    private bool _isDying;
    private float _deathElapsed;
    private float _deathAnimationDuration = 0.65f;
    private const float DeathRestDuration = 1.25f;
    private const float DeathSinkDuration = 0.85f;
    private const float DeathSinkDepth = 1.8f;

    public override bool IsDying => _isDying;
    public override bool HasDeathExplosion => false;
    public override bool IsReadyForRemoval =>
        _isDying && _deathElapsed >= _deathAnimationDuration + DeathRestDuration + DeathSinkDuration;

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
            movementProfile ?? new GroundMovementProfile(MovementModes.Walk, 50.0f))
    {
        MoveSpeed = 3.0f;
        RotationSpeed = MathHelper.TwoPi;
        // Infantry has no independently rotating turret. While attacking, the
        // body itself tracks the target at the same speed as normal turning.
        RotateBodyTowardsTarget = true;
        TargetAngleDegreesPerSecond = MathHelper.ToDegrees(RotationSpeed);

        HeadOverlayClips = new List<string> { "head:idle0", "head:idle1", "head:idle2", "head:idle3" };
        DeathClips = new List<string> { "die0", "die1", "die2" };        
        SetMesh("Soldier-2", deriveDimensions: true);

        //turret mount point for the weapon
        uint weaponSeed = BitConverter.ToUInt32(unitId.ToByteArray(), 0);
        Weapon weapon = AvailableWeapons[weaponSeed % (uint)AvailableWeapons.Length];
        SetWeapon(weapon);

        _animationPlayer = new AnimationPlayer(_meshSet!.RootMesh.Animations);        
        if (_meshSet.RootMesh.Animations.TryGetValue("die0", out MeshAnimationClip? deathClip))
            _deathAnimationDuration = Math.Max(0.05f, deathClip.DurationSeconds);
        _animationPlayer.Play("idle");
        _animationPlayer.SetRandomAnimationTime();
        _animationPlayer.Speed = 0.9f + Random.Shared.NextSingle() * 0.2f;
        //_animationPlayer.AddOverlay("pose:idleRifle", weight: 1.0f);        

        SetRandomArmsPose();
        SetRandomArmsPose();
        SetRandomHeadPose();
        SetRandomHeadPose();
    }

    public void SetRandomArmsPose()
    {                
        string clipName = ArmsOverlayClips[Random.Shared.Next(ArmsOverlayClips.Count)];
        _animationPlayer.AddOverlayTransition("arms", clipName, 0.4f + Random.Shared.NextSingle() * 0.2f);
    }

    public void SetRandomRunningArmsPose()
    {
        string clipName = RunArmsOverlayClips[Random.Shared.Next(RunArmsOverlayClips.Count)];
        _animationPlayer.AddOverlayTransition("arms", clipName, 0.3f + Random.Shared.NextSingle() * 0.2f);
    }

    public string GetRandomFireClip()
    {
        string clipName = FireClips[Random.Shared.Next(FireClips.Count)];
        return clipName;
    }

    public string GetRandomDeathClip()
    {
        string clipName = DeathClips[Random.Shared.Next(DeathClips.Count)];
        return clipName;
    }

    public void SetRandomHeadPose()
    {
        string clipName = HeadOverlayClips[Random.Shared.Next(HeadOverlayClips.Count)];
        _animationPlayer.AddOverlayTransition("head", clipName, 0.3f + Random.Shared.NextSingle() * 0.2f);
    }

    public void UpdateMinigun(GameTime gameTime)
    {
        float elapsedSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_currentUnitState == UnitActionState.Aiming)
        {
            _minigunRotationSpeed = Math.Min(
                _minigunRotationMaxSpeed,
                _minigunRotationSpeed + _minigunRotationAcceleration * elapsedSeconds);
        }
        else
        {
            _minigunRotationSpeed = Math.Max(
                0.0f,
                _minigunRotationSpeed - _minigunRotationAcceleration * elapsedSeconds);
        }

        _minigunRotationDegrees = MathHelper.WrapAngle(MathHelper.ToRadians(
            _minigunRotationDegrees + _minigunRotationSpeed * elapsedSeconds));
        _minigunRotationDegrees = MathHelper.ToDegrees(_minigunRotationDegrees);
    }

    public override void Update(GameTime gameTime)
    {
        if (_isDying)
        {
            _currentUnitState = UnitActionState.Dying;
            _deathElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
            _animationPlayer.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
            return;
        }

        UpdateMinigun(gameTime);
        if (_launcherReloadRemaining > 0.0f)
        {
            _launcherReloadRemaining -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_launcherReloadRemaining <= 0.0f)
                SetLauncherProjectileVisible(true);
        }
        base.Update(gameTime);


        var lastUnitState = _currentUnitState;

        bool isMoving = PlannedPath.Count > 0 || IsLeavingBuilding;

        bool hasAttackOrder =
            AttackTargetId is not null ||
            AttackGroundTarget is not null;
        bool isAiming = hasAttackOrder && !isMoving;

        if (IsLeavingBuilding)
            _currentUnitState = UnitActionState.Spawning;
        else if (isMoving)
            _currentUnitState = UnitActionState.Moving;
        else if (isAiming)
            _currentUnitState = UnitActionState.Aiming;
        else
            _currentUnitState = UnitActionState.Idle;

        if (lastUnitState != _currentUnitState)
        {
            if (lastUnitState == UnitActionState.Aiming)
            {
                _animationPlayer.AddOverlayTransition("arms", _animationPlayer.CurrentClipName, 0.0f); // <- force this clip as the transition-
                //SetRandomArmsPose();
                _animationPlayer.Play("idle");
            }

            if (_currentUnitState is UnitActionState.Moving or UnitActionState.Spawning)
            {
                _animationPlayer.Play("run");
                SetRandomRunningArmsPose();
            }
            else if (_currentUnitState == UnitActionState.Idle)
            {
                SetRandomArmsPose();
                _animationPlayer.Play("idle");
            }
            else if (_currentUnitState == UnitActionState.Aiming)
            {
                _animationPlayer.RemoveOverlayLayer("arms");
                _animationPlayer.RemoveOverlayLayer("head");
                _animationPlayer.Play(GetRandomFireClip());
            }
        }

        if (_currentUnitState == UnitActionState.Idle)
        {
            _nextIdlePoseTimer -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_nextIdlePoseTimer <= 0.0f)
            {
                if (Random.Shared.Next(10) <= 3)                
                    SetRandomArmsPose();
                if (Random.Shared.Next(10) <= 6)
                    SetRandomHeadPose();

                _nextIdlePoseTimer = 3.0f + Random.Shared.NextSingle() * 5.0f;
            }
        }

        _animationPlayer.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public override bool BeginDeathSequence()
    {
        if (_isDying)
            return true;

        _isDying = true;
        _deathElapsed = 0.0f;
        IsSelected = false;
        Stop();
        _animationPlayer.ClearOverlays();
        _animationPlayer.Play(GetRandomDeathClip(), restart: true);
        return true;
    }

    protected override void OnBeginLeavingBuilding()
    {
        SetRandomRunningArmsPose();
        _animationPlayer.Play("run", restart: true);
    }

    protected override void OnFinishedLeavingBuilding()
    {
        SetRandomArmsPose();
        _animationPlayer.Play("idle", restart: true);
    }

    public override void PlayShotEffects()
    {
        if (_isDying)
            return;
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

        if (EquippedWeapon == Weapon.RPG)
        {
            _launcherReloadRemaining = AttackCooldown;
            SetLauncherProjectileVisible(false);
            Globals.World.Particles.EmitSmoke(
                muzzlePosition,
                barrelDirection,
                SmokeEmissionPresets.RpgMuzzle());

            if (TryGetAnimatedPivotWorldTransform("pivot:exhaust", out Matrix exhaustWorld))
            {
                Globals.World.Particles.EmitSmoke(
                    exhaustWorld.Translation,
                    -barrelDirection,
                    SmokeEmissionPresets.RpgBackblast());
            }
            return;
        }

        Globals.World.Particles.EmitRifleMuzzleFlash(muzzlePosition, barrelDirection);
    }

    public override void Draw(Effect effect)
    {
        float sinkProgress = _isDying
            ? MathHelper.Clamp(
                (_deathElapsed - _deathAnimationDuration - DeathRestDuration) / DeathSinkDuration,
                0.0f,
                1.0f)
            : 0.0f;
        Matrix deathSink = Matrix.CreateTranslation(Vector3.Down * (DeathSinkDepth * sinkProgress));

        // Weapon attachments own their parameters. If the equipped weapon has
        // no pivot:barrel (or no weapon is attached), this is simply ignored.
        _meshSet?.SetAttachmentParameter(
            "pivot:gun",
            "pivot:barrel",
            MathHelper.ToRadians(_minigunRotationDegrees));

        EffectParameter? opacity = effect.Parameters["Opacity"];
        opacity?.SetValue(1.0f - sinkProgress);
        try
        {
            _meshSet?.Draw(effect, GetVisualWorldMatrix() * deathSink, GetMeshAnimationPose());
        }
        finally
        {
            // Unit.fx is shared by every object drawn afterwards.
            opacity?.SetValue(1.0f);
        }
    }

    protected override AnimationPose GetMeshAnimationPose() => _animationPlayer.EvaluatePose();
    
    void SetWeaponType_Rifle()
    {        
        ArmsOverlayClips = new List<string> { "arms:idleRifle0", "arms:idleRifle1", "arms:idleRifle2", "arms:idleRifle3" };
        RunArmsOverlayClips = new List<string> { "arms:idleRifle1", "arms:idleRifle2" };
        FireClips = new List<string> { "fire:Rifle0", "fire:Rifle1" };
    }

    void SetWeaponType_Pistol()
    {        
        ArmsOverlayClips = new List<string> { "arms:idlePistol0", "arms:idlePistol1", "arms:idlePistol2" };
        RunArmsOverlayClips = new List<string> { "arms:idlePistol2" };
        FireClips = new List<string> { "fire:Pistol0", "fire:Pistol1" };
    }

    void SetWeaponType_Minigun()
    {        
        ArmsOverlayClips = new List<string> { "arms:idleMinigun0", "arms:idleMinigun1", "arms:idleMinigun2" };
        RunArmsOverlayClips = new List<string> { "arms:idleMinigun1", "arms:idleMinigun2" };
        FireClips = new List<string> { "fire:Minigun0"};
    }

    void SetWeaponType_Launcher()
    {        
        ArmsOverlayClips = new List<string> { "arms:idleLauncher0", "arms:idleLauncher1" };
        RunArmsOverlayClips = new List<string> { "arms:idleLauncher0", "arms:idleLauncher1" };
        FireClips = new List<string> { "fire:Launcher0", "fire:Launcher1"};
    }

    public void SetWeapon(Weapon weaponType)
    {
        EquippedWeapon = weaponType;
        _launcherReloadRemaining = 0.0f;
        SetLauncherProjectileVisible(true);
        switch (weaponType)
        {
            case Weapon.Ak47:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["ak47"]);
                this.AttackCooldown = 0.2f;
                this.AttackDamage = 25.0f;
                break;
            case Weapon.BredaM1935PG:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["breda-m1935pg"]);
                this.AttackCooldown = 0.2f;
                this.AttackDamage = 15.0f;
                break;
            case Weapon.Brok17:
                SetWeaponType_Pistol();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["brok17"]);
                this.AttackCooldown = 0.5f;
                this.AttackDamage = 25.0f;
                break;
            case Weapon.KraberAPSniper:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["kraber-ap-sniper"]);
                this.AttackCooldown = 5.0f;
                this.AttackDamage = 200.0f;
                break;                
            case Weapon.HuntingRifle:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["hunting"]);
                this.AttackCooldown = 5.0f;
                this.AttackDamage = 200.0f;
                break;                
            case Weapon.M16:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["m16"]);
                this.AttackCooldown = 0.2f;
                this.AttackDamage = 25.0f;
                break;
            case Weapon.AR15:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["ar-15"]);
                this.AttackCooldown = 0.2f;
                this.AttackDamage = 25.0f;
                break;
            case Weapon.StenMk2Apocalypse:
                SetWeaponType_Rifle();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["sten-mk2-apocalypse"]);
                this.AttackCooldown = 0.2f;
                this.AttackDamage = 25.0f;
                break;
            case Weapon.UziMac10:
                SetWeaponType_Pistol();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["uzi-mac-10"]);
                this.AttackCooldown = 0.3f;
                this.AttackDamage = 15.0f;
                break;
            case Weapon.Minigun:
                SetWeaponType_Minigun();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["minigun"]);
                this.AttackCooldown = 0.01f;
                this.AttackDamage = 25.0f;
                break;
            case Weapon.RPG:
                SetWeaponType_Launcher();
                _meshSet?.SetAttachment("pivot:gun", Globals.MeshHandler.Meshes["rpg"]);
                this.AttackCooldown = 2.00f;
                this.AttackDamage = 150.0f;
                this.AttackRange = 50;
                break;
        }
    }

    private void SetLauncherProjectileVisible(bool visible)
    {
        float value = visible ? 1.0f : 0.0f;
        _meshSet?.SetAttachmentParameter("pivot:gun", "visibility:pivot:projectile", value);
    }
}
