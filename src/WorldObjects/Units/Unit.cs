using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum UnitBehavior
{
    Aggressive,
    Passive
}

public abstract class Unit : WorldObject
{
    public enum UnitActionState
    {
        Idle,
        Spawning,
        Moving,
        Aiming,
        Dying
    }
    protected UnitActionState _currentUnitState = UnitActionState.Idle;    
    public Guid UnitId { get; }
    public virtual bool SupportsRallyPoint => false;
    public Vector3? RallyPoint { get; private set; }
    public uint RallyPointRevision { get; private set; }

    public RallyPointState GetRallyPointState() => RallyPoint is Vector3 position
        ? new(RallyPointRevision, true, position.X, position.Y, position.Z)
        : new(RallyPointRevision, false);

    // Gameplay mutation: called only after host validation.
    internal void SetRallyPoint(Vector3? position)
    {
        if (!SupportsRallyPoint)
            return;
        ApplyRallyPointState(position is Vector3 point
            ? new(RallyPointRevision + 1, true, point.X, point.Y, point.Z)
            : new(RallyPointRevision + 1, false));
        StateRevision++;
        NetworkStateDirty = true;
    }

    public void ApplyRallyPointState(RallyPointState state)
    {
        if (!SupportsRallyPoint || state.Revision < RallyPointRevision ||
            (state.HasPosition && (!float.IsFinite(state.X) || !float.IsFinite(state.Y) || !float.IsFinite(state.Z))))
            return;
        RallyPoint = state.HasPosition ? new Vector3(state.X, state.Y, state.Z) : null;
        RallyPointRevision = state.Revision;
    }
    public float HitPoints { get; set; }
    public float MaxHitPoints { get; }
    public Guid CreatorPlayerId { get; private set; }
    /// <summary>Current owner. Null represents a neutral/capturable world unit.</summary>
    public Guid? ArmyId { get; private set; }
    public bool IsEmbarked { get; private set; }
    public Guid? ContainerUnitId { get; private set; }
    /// <summary>Optional seats/crew/garrison carried by this unit.</summary>
    public OccupancyComponent? Occupancy { get; protected set; }
    public int Length { get; protected set; }
    public int Width { get; protected set; }
    public float Height { get; protected set; }
    /// <summary>
    /// Center of the physical footprint in the mesh's local space. Imported
    /// BBModels are not required to be authored around their origin.
    /// </summary>
    public Vector3 FootprintLocalCenter { get; protected set; }
    public bool IsSelected { get; set; }
    public GotoCommand? CurrentCommand { get; protected set; }
    /// <summary>True while this unit is visually playing its death sequence.</summary>
    public virtual bool IsDying => false;
    /// <summary>Death ghosts remain drawable but cannot be selected or targeted.</summary>
    public virtual bool CanBeTargeted => !IsDying && !IsEmbarked;
    public virtual bool IsSelectable => !IsDying && !IsEmbarked;
    /// <summary>Lets a unit keep itself alive locally for a death animation.</summary>
    public virtual bool BeginDeathSequence() => false;
    /// <summary>Set by animated death units once their local visual has finished.</summary>
    public virtual bool IsReadyForRemoval => false;
    /// <summary>Whether generic destruction should create the large explosion effect.</summary>
    public virtual bool HasDeathExplosion => true;
    public virtual IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 0)
    ];
    public override string StateTypeId => "unit";
    public float AttackRange { get; set; } = 12.0f;
    /// <summary>
    /// Base damage caused by one successful attack. This is deliberately a
    /// plain field for now; individual unit constructors can simply assign
    /// their own fixed value.
    /// </summary>
    public float AttackDamage = 25.0f;
    public float AttackCooldown { get; set; } = 0.75f; // in seconds
    /// <summary>
    /// Hitscan weapons have no visible travelling projectile. The host sends a
    /// separate impact position to peers after triggering the muzzle effect.
    /// </summary>
    public virtual bool UsesHitscanWeapon => false;
    public virtual ProjectileKind ProjectileKind =>
        UsesHitscanWeapon ? ProjectileKind.None : ProjectileKind.BallisticShell;
    /// <summary>Visual and authoritative travel speed in world units per second.</summary>
    public virtual float ProjectileSpeed => 35.0f;
    /// <summary>
    /// Elevation angle of the most recently spawned projectile relative to
    /// the horizontal plane. Positive values point upward, negative values
    /// downward. This is pitch (not yaw) and can be used to align a firing
    /// animation with the host-authoritative launch direction.
    /// </summary>
    public float LastProjectilePitchDegrees { get; private set; }
    public Guid? AttackTargetId { get; private set; }
    public Vector3? AttackGroundTarget { get; private set; }
    /// <summary>Unit to keep within <see cref="FollowDistance"/> world units of.</summary>
    public Guid? FollowUnitId { get; private set; }
    /// <summary>Desired horizontal spacing to <see cref="FollowUnitId"/>.</summary>
    public float FollowDistance { get; private set; }
    /// <summary>Normalized atlas offset used when sampling the visible unit texture.</summary>
    public Vector2 UnitTextureUVOffset { get; set; } = Vector2.Zero;
    public UnitBehavior Behavior { get; set; } = UnitBehavior.Aggressive;
    protected MeshSet? _meshSet;
    // Authored local bounds, without grid rounding or placement padding.
    private BoundingBox? _selectionBounds;
    private float _exhaustElapsed;

    public bool TryGetEntryWorldPosition(out Vector3 position) =>
        TryGetContainerPivotPosition("pivot:entry", "pivot:exit", out position);

    public bool TryGetExitWorldPosition(out Vector3 position) =>
        TryGetContainerPivotPosition("pivot:exit", "pivot:entry", out position);

    private bool TryGetContainerPivotPosition(string primaryPivot, string fallbackPivot, out Vector3 position)
    {
        Matrix world = GetWorldMatrix();
        if (_meshSet?.TryGetPivotWorldPosition(primaryPivot, world, out position) == true ||
            _meshSet?.TryGetPivotWorldPosition(fallbackPivot, world, out position) == true)
            return true;

        Vector3 right = Transform.Right;
        right.Y = 0.0f;
        if (right.LengthSquared() <= 0.0001f)
            right = Vector3.Right;
        else
            right.Normalize();
        position = Position + right * (Width * Globals.World.GameGrid.CellSize * 0.5f + 1.0f);
        return false;
    }

    /// <summary>Local visual offset applied to the rendered model only.</summary>
    public Vector3 VisualRecoilOffset { get; private set; }
    /// <summary>Local pitch applied to the rendered model only.</summary>
    public float VisualRecoilPitchDegrees { get; private set; }
    /// <summary>
    /// Local ground-level point around which the model nicks during recoil.
    /// It is normally below the turret, so the tracks appear planted.
    /// </summary>
    public Vector3 VisualRecoilPivot { get; set; } = Vector3.Zero;
    /// <summary>
    /// Fraction of the remaining recoil recovered per nominal 60 FPS frame.
    /// A value of 0.05 moves five percent towards the resting pose each frame.
    /// </summary>
    public float VisualRecoilRecoveryFactor { get; set; } = 0.05f;
    /// <summary>
    /// Uses the current local target/turret angle to orient the body recoil.
    /// Set this to false for weapons that are rigidly mounted facing forward.
    /// </summary>
    public bool VisualRecoilFollowsTargetAngle { get; set; } = true;
    public Matrix VisualRecoilTransform
    {
        get
        {
            float aimAngleRadians = VisualRecoilFollowsTargetAngle
                ? MathHelper.ToRadians(TargetAngleDegrees)
                : 0.0f;
            Matrix aimRotation = Matrix.CreateRotationY(aimAngleRadians);

            // X is the pitch axis of a forward-facing barrel. Rotate it into
            // the turret's current local direction before tilting the hull.
            Vector3 pitchAxis = Vector3.TransformNormal(Vector3.Right, aimRotation);
            Vector3 offset = Vector3.TransformNormal(VisualRecoilOffset, aimRotation);
            Matrix rotationAtGroundPivot =
                Matrix.CreateTranslation(-VisualRecoilPivot) *
                Matrix.CreateFromAxisAngle(
                    pitchAxis,
                    MathHelper.ToRadians(VisualRecoilPitchDegrees)) *
                Matrix.CreateTranslation(VisualRecoilPivot);
            return rotationAtGroundPivot *
                Matrix.CreateTranslation(offset);
        }
    }

    // -----------------------------------------------------------------------
    // Visual targeting / aiming model
    // -----------------------------------------------------------------------
    // A Unit may visually target either another Unit (TargetUnitId) or a terrain
    // position (TargetTerrainCell plus the internally stored world position).
    // SetTarget... only changes visual aiming. SetAttack... changes both visual
    // aiming and the host-authoritative attack state used by TryQueueShot.
    //
    // TargetAngleDegrees is a LOCAL yaw relative to the unit body's forward
    // direction. It is deliberately normalized to [-180°, +180°]. A mesh node
    // using Mesh.TurretAngle must convert it to radians at draw time:
    //     mesh.SetParameter(Mesh.TurretAngle,
    //         MathHelper.ToRadians(unit.TargetAngleDegrees));
    //
    // UpdateTargetAngle runs after body movement, follows moving targets, and
    // returns the angle smoothly to 0° when no target remains. The min/max
    // degree values describe the permitted continuous turret arc. An arc below
    // 360° never crosses its stop; it may therefore use the longer legal turn.
    // Before an authoritative host queues a shot, use IsTargetAimed(tolerance)
    // when the weapon must not fire until its visual part is aligned.
    // -----------------------------------------------------------------------

    /// <summary>The unit currently being visually targeted, if any.</summary>
    public Guid? TargetUnitId { get; private set; }
    /// <summary>
    /// Host-assigned defensive target. It is used for aiming and shooting but
    /// intentionally never feeds MobileUnit's attack-movement/chase logic.
    /// </summary>
    public Guid? TemporaryTargetUnitId { get; private set; }
    /// <summary>The terrain cell currently being visually targeted, if any.</summary>
    public Point? TargetTerrainCell { get; private set; }
    /// <summary>
    /// Current local yaw towards the target, in degrees. Values are always in
    /// the range -180 to +180 degrees.
    /// </summary>
    public float TargetAngleDegrees { get; private set; }
    public float TargetAngleDegreesPerSecond { get; set; } = 50.0f;
    /// <summary>
    /// When enabled, aiming turns the unit's complete world transform instead
    /// of a local turret/head mesh part. Use this for infantry and other
    /// units whose weapon is fixed to the body.
    /// </summary>
    public bool RotateBodyTowardsTarget { get; set; }
    /// <summary>
    /// Lowest permitted local target angle in degrees. The default permits a
    /// full turret rotation; values such as -120 limit a vehicle turret.
    /// </summary>
    public float TargetAngleMinimumDegrees { get; set; } = -45.0f;
    /// <summary>Highest permitted local target angle in degrees.</summary>
    public float TargetAngleMaximumDegrees { get; set; } = 45.0f;
    private double _nextShotTime;
    private Vector3? _targetTerrainPosition;
    public bool IsDamaged => HitPoints < MaxHitPoints;

    public Unit(
        Vector3 position,
        int length,
        int width,
        float height,
        Guid unitId
        ) : base(position)
    {
        IsNetworkObject = true;
        HitPoints = MaxHitPoints = 100.0f;
        UnitId = unitId;
        Length = length;
        Width = width;
        Height = height;
    }

    internal void SetCreatorPlayer(Guid creatorPlayerId)
    {
        CreatorPlayerId = creatorPlayerId;
    }

    internal void SetArmy(Guid? armyId) => ArmyId = armyId;

    internal void Embark(Guid containerUnitId)
    {
        Stop();
        IsSelected = false;
        IsEmbarked = true;
        ContainerUnitId = containerUnitId;
    }

    internal void Disembark(Vector3 position)
    {
        IsEmbarked = false;
        ContainerUnitId = null;
        SetPosition(position);
    }

    /// <summary>Assigns one mesh and optionally derives conservative grid dimensions from it.</summary>
    protected void SetMesh(string meshName, bool deriveDimensions = false, float padding = 0.0f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meshName);
        SetMeshSet(new MeshSet(Globals.MeshHandler.Meshes[meshName]), deriveDimensions, padding);
    }

    /// <summary>Assigns a composed mesh and optionally derives conservative grid dimensions from its bounds.</summary>
    protected void SetMeshSet(MeshSet meshSet, bool deriveDimensions = false, float padding = 0.0f)
    {
        ArgumentNullException.ThrowIfNull(meshSet);
        if (padding < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(padding));

        _meshSet = meshSet;
        BoundingBox bounds = meshSet.GetBounds();
        _selectionBounds = bounds;
        if (!deriveDimensions)
            return;

        Vector3 size = bounds.Max - bounds.Min + new Vector3(padding * 2.0f);
        FootprintLocalCenter = (bounds.Min + bounds.Max) * 0.5f;
        Width = Math.Max(1, (int)MathF.Ceiling(size.X));
        Length = Math.Max(1, (int)MathF.Ceiling(size.Z));
        Height = Math.Max(0.01f, size.Y);
    }

    /// <summary>Returns the world-space center used for the grid footprint.</summary>
    public Vector3 GetFootprintCenter(Vector3 position, float rotationDegrees)
    {
        Matrix rotation = Matrix.CreateRotationY(MathHelper.ToRadians(rotationDegrees));
        return position + Vector3.TransformNormal(FootprintLocalCenter, rotation);
    }

    /// <summary>
    /// Tests a proposed world position and yaw in degrees before placing a
    /// unit/building. Mobile units use a 90°-snapped grid footprint while
    /// buildings retain their freely rotated footprint.
    /// </summary>
    public bool CanPlace(
        Vector3 position,
        float rotationDegrees,
        float maximumTerrainHeightDifference = 2.0f)
    {
        if (maximumTerrainHeightDifference < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(maximumTerrainHeightDifference));

        GameGrid grid = Globals.World.GameGrid;
        if (!grid.CanPlace(this, position, rotationDegrees))
            return false;

        IReadOnlyList<Point> footprintCells = grid.GetFootprintCells(this, position, rotationDegrees);
        if (footprintCells.Count == 0)
            return false;

        float minimumHeight = float.MaxValue;
        float maximumHeight = float.MinValue;
        foreach (Point cell in footprintCells)
        {
            // Grid bounds were already checked above; keeping the guard makes
            // this method safe if the Grid implementation changes later.
            if (cell.X < 0 || cell.Y < 0 || cell.X >= grid.Width || cell.Y >= grid.Height)
                return false;
            float height = Globals.World.Terrain.GetHeight(cell.X, cell.Y);
            minimumHeight = MathF.Min(minimumHeight, height);
            maximumHeight = MathF.Max(maximumHeight, height);
        }

        return maximumHeight - minimumHeight <= maximumTerrainHeightDifference;
    }

    public virtual void ClearCommand()
    {
        CurrentCommand = null;
    }

    public virtual UnitState GetState()
    {
        return new UnitState(
            UnitId,
            Revision: 0,
            StateTypeId,
            StateVersion,
            Array.Empty<byte>());
    }

    public virtual void ApplyState(UnitState state)
    {
        // Units without specialized state intentionally have no payload to apply.
    }

    public virtual void Stop()
    {
        ClearCommand();
        AttackTargetId = null;
        AttackGroundTarget = null;
        ClearFollowUnit();
        ClearTarget();
    }

    // Gameplay mutation: only the host invokes this method.
    public virtual bool OnHit(HitInfo hit)
    {
        HitPoints = Math.Max(0.0f, HitPoints - hit.Damage);
        return HitPoints <= 0.0f;
    }

    // Clients use the host-provided value for display only.
    public void ApplyHitPoints(float hitPoints)
    {
        HitPoints = Math.Clamp(hitPoints, 0.0f, MaxHitPoints);
    }

    public virtual void PlayHitEffects(HitInfo hit)
    {
    }

    /// <summary>Runs local visual/audio feedback when the host replicated a shot.</summary>
    public virtual void PlayShotEffects()
    {
    }

    /// <summary>
    /// Records the actual replicated launch direction of a projectile. The
    /// horizontal component is X/Z; Y determines its elevation.
    /// </summary>
    internal void SetProjectileLaunchVelocity(Vector3 velocity)
    {
        float horizontalSpeed = MathF.Sqrt(
            velocity.X * velocity.X + velocity.Z * velocity.Z);

        if (horizontalSpeed <= 0.0001f && MathF.Abs(velocity.Y) <= 0.0001f)
            return;

        LastProjectilePitchDegrees = MathHelper.ToDegrees(
            MathF.Atan2(velocity.Y, horizontalSpeed));
        OnProjectilePitchChanged(LastProjectilePitchDegrees);
    }

    /// <summary>
    /// Optional visual hook for units that want to immediately apply the
    /// launch pitch to an animation or mesh node.
    /// </summary>
    protected virtual void OnProjectilePitchChanged(float pitchDegrees)
    {
    }

    /// <summary>
    /// Applies a local-only body recoil. It intentionally does not affect the
    /// unit transform, movement, collision or replicated game state.
    /// </summary>
    public void TriggerVisualRecoil(Vector3 localOffset, float pitchDegrees)
    {
        VisualRecoilOffset = localOffset;
        VisualRecoilPitchDegrees = -pitchDegrees;
    }

    /// <summary>Starts a continuous attack against another unit.</summary>
    public void SetAttackTarget(Guid targetId)
    {
        AttackTargetId = targetId;
        AttackGroundTarget = null;
        SetTargetUnit(targetId);
        SetFollowUnitInternal(targetId, AttackRange);
    }

    /// <summary>Starts a continuous attack against a terrain position.</summary>
    public void SetAttackGroundTarget(Vector3 target)
    {
        AttackGroundTarget = target;
        AttackTargetId = null;
        ClearFollowUnit();
        SetTargetTerrain(target);
    }

    /// <summary>
    /// Clears commands and visual targeting that refer to a unit which has
    /// just been removed from the world. Called centrally by UnitHandler so
    /// host and clients converge as soon as a DestroyUnitCommand is applied.
    /// </summary>
    public void ClearReferencesToDestroyedUnit(Guid destroyedUnitId)
    {
        if (AttackTargetId == destroyedUnitId)
            AttackTargetId = null;

        if (FollowUnitId == destroyedUnitId)
            ClearFollowUnit();

        if (TargetUnitId == destroyedUnitId)
            ClearTarget();
        else if (TemporaryTargetUnitId == destroyedUnitId)
            ClearTemporaryTarget();
    }

    /// <summary>
    /// Starts a non-attacking follow order. The spacing is captured when the
    /// host command is applied, so the unit preserves the player's formation.
    /// </summary>
    public bool SetFollowUnit(Guid targetId)
    {
        Unit? target = Globals.World.Units.FindById(targetId);
        if (target is null || target == this)
            return false;

        Vector2 offset = new(target.Position.X - Position.X, target.Position.Z - Position.Z);
        AttackTargetId = null;
        AttackGroundTarget = null;
        ClearTarget();
        SetFollowUnitInternal(targetId, offset.Length());
        return true;
    }

    public void ClearFollowUnit()
    {
        FollowUnitId = null;
        FollowDistance = 0.0f;
    }

    private void SetFollowUnitInternal(Guid targetId, float distance)
    {
        FollowUnitId = targetId;
        FollowDistance = Math.Max(0.0f, distance);
    }

    public void SetTargetUnit(Guid targetId)
    {
        TargetUnitId = targetId;
        TargetTerrainCell = null;
        _targetTerrainPosition = null;
        ClearTemporaryTarget();
    }

    public void SetTargetTerrainCell(Point targetCell)
    {
        Vector3 target = Globals.World.GameGrid.ToWorldPosition(targetCell, 0.0f);
        target.Y = Globals.World.Terrain.GetHeight(targetCell.X, targetCell.Y);
        SetTargetTerrain(target, targetCell);
    }

    public void SetTargetTerrain(Vector3 target)
    {
        SetTargetTerrain(target, Globals.World.GameGrid.ToCell(target));
    }

    public void ClearTarget()
    {
        TargetUnitId = null;
        TargetTerrainCell = null;
        _targetTerrainPosition = null;
        ClearTemporaryTarget();
    }

    /// <summary>Only the host assigns temporary targets, then replicates them to clients.</summary>
    public bool SetTemporaryTarget(Guid targetId)
    {
        if (HasExplicitTarget || TemporaryTargetUnitId == targetId)
            return false;

        TemporaryTargetUnitId = targetId;
        return true;
    }

    public bool ClearTemporaryTarget()
    {
        if (TemporaryTargetUnitId is null)
            return false;

        TemporaryTargetUnitId = null;
        return true;
    }

    public bool HasExplicitTarget =>
        AttackTargetId is not null ||
        AttackGroundTarget is not null ||
        TargetUnitId is not null ||
        TargetTerrainCell is not null;

    /// <summary>Central extension point for team, visibility and priority rules.</summary>
    public bool IsEnemy(Unit other)
    {
        if (other == this || ArmyId is not Guid armyId || other.ArmyId is not Guid otherArmyId)
            return false;
        if (armyId == otherArmyId)
            return false;

        Army? army = Globals.Game.Armies.Find(armyId);
        Army? otherArmy = Globals.Game.Armies.Find(otherArmyId);
        Player? owner = army is null
            ? null
            : Globals.Game.Players.FirstOrDefault(player => army.OwnerPlayerIds.Contains(player.Id));
        Player? otherOwner = otherArmy is null
            ? null
            : Globals.Game.Players.FirstOrDefault(player => otherArmy.OwnerPlayerIds.Contains(player.Id));
        return owner is null || otherOwner is null || owner.TeamId != otherOwner.TeamId;
    }

    public bool IsAlly(Unit other)
    {
        return (IsSameTeam(other));
    }

    public bool IsSamePlayer(Unit other)
    {
        return other == this || other.CreatorPlayerId == CreatorPlayerId;
    }

    public bool IsLocalPlayer()
    {
        return CreatorPlayerId == Globals.Game.Network.LocalPeerId;
    }

    public bool IsSameTeam(Unit other)
    {
        if (other == this)
            return true;
        if (ArmyId is not Guid armyId || other.ArmyId is not Guid otherArmyId)
            return false;
        if (armyId == otherArmyId)
            return true;

        Army? army = Globals.Game.Armies.Find(armyId);
        Army? otherArmy = Globals.Game.Armies.Find(otherArmyId);
        Player? owner = army is null
            ? null
            : Globals.Game.Players.FirstOrDefault(player => army.OwnerPlayerIds.Contains(player.Id));
        Player? otherOwner = otherArmy is null
            ? null
            : Globals.Game.Players.FirstOrDefault(player => otherArmy.OwnerPlayerIds.Contains(player.Id));
        return owner is not null && otherOwner is not null && owner.TeamId == otherOwner.TeamId;
    }

    /// <summary>Central extension point evaluated by the host before a defensive target is assigned.</summary>
    public virtual bool ShouldAttack(Unit candidate) =>
        !IsDying && !IsEmbarked && candidate.CanBeTargeted && Behavior == UnitBehavior.Aggressive && IsEnemy(candidate);

    /// <summary>
    /// Advances the local mesh angle towards the current target. Call this
    /// after a unit's body transform has been updated for the frame.
    /// </summary>
    public void UpdateTargetAngle(GameTime gameTime)
    {
        Vector3? targetPosition = GetTargetPosition();

        if (RotateBodyTowardsTarget)
        {
            if (targetPosition is not Vector3 bodyTarget)
            {
                // A body-aiming unit has no independent aim part to return.
                TargetAngleDegrees = 0.0f;
                return;
            }

            Vector3 desiredDirection = bodyTarget - Position;
            desiredDirection.Y = 0.0f;
            if (desiredDirection.LengthSquared() <= 0.0001f)
                return;
            desiredDirection.Normalize();

            Vector3 bodyForward = Vector3.TransformNormal(Vector3.Forward, Transform);
            bodyForward.Y = 0.0f;
            if (bodyForward.LengthSquared() <= 0.0001f)
                return;
            bodyForward.Normalize();

            float requiredTurnRadians = MathF.Atan2(
                Vector3.Cross(bodyForward, desiredDirection).Y,
                Vector3.Dot(bodyForward, desiredDirection));
            float maximumTurnRadians = MathHelper.ToRadians(TargetAngleDegreesPerSecond) *
                (float)gameTime.ElapsedGameTime.TotalSeconds;
            float appliedTurnRadians = MathHelper.Clamp(
                requiredTurnRadians,
                -maximumTurnRadians,
                maximumTurnRadians);

            Transform = Matrix.CreateRotationY(appliedTurnRadians) * Transform;
            // Weapon, muzzle and shot direction use the unit transform now;
            // there is deliberately no residual turret angle.
            TargetAngleDegrees = 0.0f;
            return;
        }

        if (targetPosition is not Vector3 target)
        {
            // No target: return the visual aiming part (turret, head, ...) to
            // its neutral local angle, which is the unit's forward direction.
            float returnStep = TargetAngleDegreesPerSecond *
                (float)gameTime.ElapsedGameTime.TotalSeconds;
            float neutralAngle = ClampTargetAngleDegrees(0.0f);
            TargetAngleDegrees = ClampTargetAngleDegrees(TargetAngleDegrees + MoveTargetAngleTowardsDegrees(
                TargetAngleDegrees,
                neutralAngle,
                returnStep));
            return;
        }

        {
            Vector3 desiredDirection = target - Position;
            desiredDirection.Y = 0.0f;
            if (desiredDirection.LengthSquared() <= 0.0001f)
                return;
            desiredDirection.Normalize();

            Vector3 bodyForward = Vector3.TransformNormal(Vector3.Forward, Transform);
            bodyForward.Y = 0.0f;
            if (bodyForward.LengthSquared() <= 0.0001f)
                return;
            bodyForward.Normalize();

            float bodyYawDegrees = DirectionToAngleDegrees(bodyForward);
            float desiredWorldYawDegrees = DirectionToAngleDegrees(desiredDirection);
            float desiredLocalAngleDegrees = ClampTargetAngleDegrees(
                WrapAngleDegrees(desiredWorldYawDegrees - bodyYawDegrees));
            float currentWorldYawDegrees = bodyYawDegrees + TargetAngleDegrees;
            float maximumStep = TargetAngleDegreesPerSecond *
                (float)gameTime.ElapsedGameTime.TotalSeconds;
            float nextWorldYawDegrees = currentWorldYawDegrees + MoveTargetAngleTowardsDegrees(
                TargetAngleDegrees,
                desiredLocalAngleDegrees,
                maximumStep);

            TargetAngleDegrees = ClampTargetAngleDegrees(nextWorldYawDegrees - bodyYawDegrees);
        }
    }

    /// <summary>
    /// Returns true when a valid target lies inside the turret's rotation arc
    /// and the current angle is within <paramref name="toleranceDegrees"/>.
    /// </summary>
    public bool IsTargetAimed(float toleranceDegrees = 2.0f)
    {
        if (toleranceDegrees < 0.0f || !TryGetDesiredLocalAngleDegrees(out float desiredAngleDegrees))
            return false;

        return MathF.Abs(WrapAngleDegrees(desiredAngleDegrees - TargetAngleDegrees)) <=
            toleranceDegrees;
    }

    public bool IsReadyToShoot(double hostTime) // this is a host function
    {
        if (IsDying)
            return false;
        if (hostTime < _nextShotTime)
            return false;

        if (GetTargetPosition() is not Vector3 target)
            return false;

        if (!IsTargetAimed())
            return false;

        return true;
    }

    private Vector3? GetTargetPosition()
    {
        if (TargetUnitId is Guid targetUnitId)
        {
            Unit? targetUnit = Globals.World.Units.FindById(targetUnitId);
            if (targetUnit is not null)
                return targetUnit.Position;

            ClearTarget();
            return null;
        }

        if (TemporaryTargetUnitId is Guid temporaryTargetId)
        {
            Unit? temporaryTarget = Globals.World.Units.FindById(temporaryTargetId);
            if (temporaryTarget is not null)
                return temporaryTarget.Position;

            TemporaryTargetUnitId = null;
        }

        return _targetTerrainPosition;
    }

    private void SetTargetTerrain(Vector3 target, Point targetCell)
    {
        TargetUnitId = null;
        TargetTerrainCell = targetCell;
        _targetTerrainPosition = target;
    }

    private bool TryGetDesiredLocalAngleDegrees(out float desiredAngleDegrees)
    {
        desiredAngleDegrees = 0.0f;
        if (GetTargetPosition() is not Vector3 target)
            return false;

        Vector3 desiredDirection = target - Position;
        desiredDirection.Y = 0.0f;
        if (desiredDirection.LengthSquared() <= 0.0001f)
            return false;
        desiredDirection.Normalize();

        Vector3 bodyForward = Vector3.TransformNormal(Vector3.Forward, Transform);
        bodyForward.Y = 0.0f;
        if (bodyForward.LengthSquared() <= 0.0001f)
            return false;
        bodyForward.Normalize();

        float unconstrainedAngleDegrees = WrapAngleDegrees(
            DirectionToAngleDegrees(desiredDirection) - DirectionToAngleDegrees(bodyForward));
        float constrainedAngleDegrees = ClampTargetAngleDegrees(unconstrainedAngleDegrees);
        if (MathF.Abs(WrapAngleDegrees(constrainedAngleDegrees - unconstrainedAngleDegrees)) > 0.001f)
            return false;

        desiredAngleDegrees = constrainedAngleDegrees;
        return true;
    }

    private static float DirectionToAngleDegrees(Vector3 direction) =>
        MathHelper.ToDegrees(MathF.Atan2(-direction.X, -direction.Z));

    private static float WrapAngleDegrees(float angleDegrees)
    {
        while (angleDegrees > 180.0f)
            angleDegrees -= 360.0f;
        while (angleDegrees < -180.0f)
            angleDegrees += 360.0f;
        return angleDegrees;
    }

    private float ClampTargetAngleDegrees(float angleDegrees)
    {
        angleDegrees = WrapAngleDegrees(angleDegrees);
        if (TargetAngleMinimumDegrees > TargetAngleMaximumDegrees)
            throw new InvalidOperationException(
                "TargetAngleMinimumDegrees must not be greater than TargetAngleMaximumDegrees.");
        return MathHelper.Clamp(
            angleDegrees,
            TargetAngleMinimumDegrees,
            TargetAngleMaximumDegrees);
    }

    private float MoveTargetAngleTowardsDegrees(
        float currentDegrees,
        float targetDegrees,
        float maximumStepDegrees)
    {
        // A restricted turret must remain inside its continuous rotation arc.
        // Example: from -110° to +110° in a [-120°, +120°] arc, it must take
        // the allowed +220° path via the front instead of crossing the -120°
        // stop on the mathematically shorter -140° path.
        float deltaDegrees = TargetAngleMaximumDegrees - TargetAngleMinimumDegrees < 360.0f
            ? targetDegrees - currentDegrees
            : WrapAngleDegrees(targetDegrees - currentDegrees);

        return MathHelper.Clamp(
            deltaDegrees,
            -maximumStepDegrees,
            maximumStepDegrees);
    }

    public bool TryQueueShot(double hostTime, out Unit? target)
    {
        if (IsDying || IsEmbarked)
        {
            target = null;
            return false;
        }

        Guid? targetId = AttackTargetId ?? TemporaryTargetUnitId;
        target = targetId is Guid id ? Globals.World.Units.FindById(id) : null;
        if (target is null || !target.CanBeTargeted)
        {
            if (AttackTargetId is not null)
                AttackTargetId = null;
            else
                TemporaryTargetUnitId = null;
            return false;
        }

        Vector2 offset = new(target.Position.X - Position.X, target.Position.Z - Position.Z);
        if (offset.LengthSquared() > AttackRange * AttackRange || hostTime < _nextShotTime)
            return false;

        _nextShotTime = hostTime + AttackCooldown;
        return true;
    }

    public bool TryQueueGroundShot(double hostTime, out Vector3 target)
    {
        target = AttackGroundTarget ?? default;
        if (IsEmbarked || AttackGroundTarget is null)
            return false;

        Vector2 offset = new(target.X - Position.X, target.Z - Position.Z);
        if (offset.LengthSquared() > AttackRange * AttackRange || hostTime < _nextShotTime)
            return false;

        _nextShotTime = hostTime + AttackCooldown;
        return true;
    }

    public Rectangle GetScreenBounds(
        Matrix view,
        Matrix projection,
        Viewport viewport)
    {
        // Some older units assign _meshSet directly; capture their bounds lazily.
        if (_selectionBounds is null && _meshSet is not null)
            _selectionBounds = _meshSet.GetBounds();
        BoundingBox bounds = _selectionBounds ?? new BoundingBox(
            new Vector3(-Width * 0.5f, 0.0f, -Length * 0.5f),
            new Vector3(Width * 0.5f, Height, Length * 0.5f));
        Matrix world = GetVisualWorldMatrix();
        Point minimum = new(int.MaxValue, int.MaxValue);
        Point maximum = new(int.MinValue, int.MinValue);

        foreach (Vector3 corner in bounds.GetCorners())
        {
            Vector3 screenPosition = viewport.Project(
                corner,
                projection,
                view,
                world);
            minimum.X = Math.Min(minimum.X, (int)MathF.Floor(screenPosition.X));
            minimum.Y = Math.Min(minimum.Y, (int)MathF.Floor(screenPosition.Y));
            maximum.X = Math.Max(maximum.X, (int)MathF.Ceiling(screenPosition.X));
            maximum.Y = Math.Max(maximum.Y, (int)MathF.Ceiling(screenPosition.Y));
        }

        return new Rectangle(
            minimum.X,
            minimum.Y,
            maximum.X - minimum.X,
            maximum.Y - minimum.Y);
    }

    public override void Update(GameTime gameTime)
    {
        // Exponential easing: at 60 FPS this is exactly the configured share
        // of the remaining difference, while other frame rates feel the same.
        float recovery = 1.0f - MathF.Pow(
            1.0f - Math.Clamp(VisualRecoilRecoveryFactor, 0.0f, 1.0f),
            (float)gameTime.ElapsedGameTime.TotalSeconds * 60.0f);
        VisualRecoilOffset = Vector3.Lerp(VisualRecoilOffset, Vector3.Zero, recovery);
        VisualRecoilPitchDegrees = MathHelper.Lerp(VisualRecoilPitchDegrees, 0.0f, recovery);
        UpdateTargetAngle(gameTime);
    }

    protected Matrix GetVisualWorldMatrix() => VisualRecoilTransform * GetWorldMatrix();

    /// <summary>
    /// Emits local-only exhaust smoke from a BBModel pivot. Call this from a
    /// derived Unit's Update method; it deliberately does not affect host
    /// simulation or network state.
    /// </summary>
    protected void UpdateExhaust(
        GameTime gameTime,
        float emissionIntervalSeconds = 0.24f,
        SmokeEmissionSettings? settings = null)
    {
        if (_meshSet is null || emissionIntervalSeconds <= 0.0f)
            return;

        _exhaustElapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_exhaustElapsed < emissionIntervalSeconds)
            return;

        _exhaustElapsed %= emissionIntervalSeconds;
        if (!_meshSet.TryGetExhaustWorldPosition(GetVisualWorldMatrix(), out Vector3 exhaustPosition))
            return;

        Globals.World.Particles.EmitSmoke(
            exhaustPosition,
            Vector3.Up,
            settings ?? SmokeEmissionPresets.VehicleExhaust());
    }

    public override Matrix GetWorldMatrix()
    {
        return Matrix.CreateScale(1.0f) * Transform;
    }


    public void Select(bool isSelected = true)
    {
        IsSelected = isSelected;
    }    

    public override void Draw(Effect effect)
    {
        if (_meshSet != null)
            _meshSet.Draw(effect, GetVisualWorldMatrix());
        else
            Globals.MeshHandler.DrawMesh(effect, Globals.MeshHandler.Meshes["default"], GetVisualWorldMatrix());
    }

    public override void DrawShadow(Effect effect)
    {
        Draw(effect);
    }

    /// <summary>Returns the projectile spawn position at an empty pivot:muzzle group.</summary>
    public bool TryGetMuzzleWorldPosition(out Vector3 position)
    {
        if (TryGetAnimatedPivotWorldTransform("pivot:muzzle", out Matrix pivotWorld))
        {
            position = pivotWorld.Translation;
            return true;
        }
        return SetMissingMuzzlePosition(out position);
    }

    /// <summary>Resolves a pivot including the unit's current animation and attachments.</summary>
    public bool TryGetAnimatedPivotWorldTransform(string pivotName, out Matrix pivotWorld) =>
        _meshSet?.TryGetPivotWorldTransform(
            pivotName,
            GetVisualWorldMatrix(),
            out pivotWorld,
            GetMeshAnimationPose()) ?? SetMissingPivotTransform(out pivotWorld);

    public bool TryGetProjectileLaunchWorldTransform(out Matrix pivotWorld) =>
        TryGetAnimatedPivotWorldTransform("pivot:projectile", out pivotWorld) ||
        // Keep compatibility with the current RPG asset's misspelled pivot.
        TryGetAnimatedPivotWorldTransform("pivot:projecile", out pivotWorld) ||
        TryGetAnimatedPivotWorldTransform("pivot:muzzle", out pivotWorld);

    /// <summary>Override for units whose BBModel pivots are moved by animation.</summary>
    protected virtual AnimationPose? GetMeshAnimationPose() => null;

    private static bool SetMissingMuzzlePosition(out Vector3 position)
    {
        position = Vector3.Zero;
        return false;
    }

    private static bool SetMissingPivotTransform(out Matrix transform)
    {
        transform = Matrix.Identity;
        return false;
    }
}
