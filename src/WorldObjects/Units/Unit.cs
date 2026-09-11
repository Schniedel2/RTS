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
    public Guid UnitId { get; }
    public float HitPoints { get; set; }
    public float MaxHitPoints { get; }
    public Guid CreatorPlayerId { get; private set; }
    public int Length { get; protected set; }
    public int Width { get; protected set; }
    public float Height { get; protected set; }
    public bool IsSelected { get; set; }
    public GotoCommand? CurrentCommand { get; protected set; }
    public virtual IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 0)
    ];
    public override string StateTypeId => "unit";
    public float AttackRange { get; set; } = 12.0f;
    public float AttackCooldown { get; set; } = 0.75f; // in seconds
    public Guid? AttackTargetId { get; private set; }
    public Vector3? AttackGroundTarget { get; private set; }
    /// <summary>Unit to keep within <see cref="FollowDistance"/> world units of.</summary>
    public Guid? FollowUnitId { get; private set; }
    /// <summary>Desired horizontal spacing to <see cref="FollowUnitId"/>.</summary>
    public float FollowDistance { get; private set; }
    public UnitBehavior Behavior { get; set; } = UnitBehavior.Aggressive;

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
    /// Lowest permitted local target angle in degrees. The default permits a
    /// full turret rotation; values such as -120 limit a vehicle turret.
    /// </summary>
    public float TargetAngleMinimumDegrees { get; set; } = -360.0f;
    /// <summary>Highest permitted local target angle in degrees.</summary>
    public float TargetAngleMaximumDegrees { get; set; } = 360.0f;
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
        if (other == this || other.CreatorPlayerId == CreatorPlayerId)
            return false;

        Player? owner = Globals.Game.Players.FirstOrDefault(player => player.Id == CreatorPlayerId);
        Player? otherOwner = Globals.Game.Players.FirstOrDefault(player => player.Id == other.CreatorPlayerId);
        return owner is not null && otherOwner is not null && owner.TeamId != otherOwner.TeamId;
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
        if (other == this || other.CreatorPlayerId == CreatorPlayerId)
            return true;
        Player? owner = Globals.Game.Players.FirstOrDefault(player => player.Id == CreatorPlayerId);
        Player? otherOwner = Globals.Game.Players.FirstOrDefault(player => player.Id == other.CreatorPlayerId);
        return owner is not null && otherOwner is not null && owner.TeamId == otherOwner.TeamId;
    }

    /// <summary>Central extension point evaluated by the host before a defensive target is assigned.</summary>
    public virtual bool ShouldAttack(Unit candidate) =>
        Behavior == UnitBehavior.Aggressive && IsEnemy(candidate);

    /// <summary>
    /// Advances the local mesh angle towards the current target. Call this
    /// after a unit's body transform has been updated for the frame.
    /// </summary>
    public void UpdateTargetAngle(GameTime gameTime)
    {
        Vector3? targetPosition = GetTargetPosition();
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
        Guid? targetId = AttackTargetId ?? TemporaryTargetUnitId;
        target = targetId is Guid id ? Globals.World.Units.FindById(id) : null;
        if (target is null)
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
        if (AttackGroundTarget is null)
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
        BoundingBox bounds = new(
            new Vector3(-Width * 0.5f, 0.0f, -Length * 0.5f),
            new Vector3(Width * 0.5f, Height, Length * 0.5f));
        Point minimum = new(int.MaxValue, int.MaxValue);
        Point maximum = new(int.MinValue, int.MinValue);

        foreach (Vector3 corner in bounds.GetCorners())
        {
            Vector3 screenPosition = viewport.Project(
                corner,
                projection,
                view,
                Transform);
            int screenX = (int)screenPosition.X;
            int screenY = (int)screenPosition.Y;
            minimum.X = Math.Min(minimum.X, screenX);
            minimum.Y = Math.Min(minimum.Y, screenY);
            maximum.X = Math.Max(maximum.X, screenX);
            maximum.Y = Math.Max(maximum.Y, screenY);
        }

        return new Rectangle(
            minimum.X,
            minimum.Y,
            maximum.X - minimum.X + 1,
            maximum.Y - minimum.Y + 1);
    }

    protected override Matrix GetWorldMatrix()
    {
        return Matrix.CreateScale(1.0f) * Transform;
    }


    public void Select(bool isSelected = true)
    {
        IsSelected = isSelected;
    }    

    public override void Draw(Effect effect)
    {
        Globals.MeshHandler.DrawMesh(effect, Globals.MeshHandler.Meshes["default"], GetWorldMatrix());
    }

    public override void DrawShadow(Effect effect)
    {
        Draw(effect);
    }
}
