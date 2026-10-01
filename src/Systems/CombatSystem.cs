using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

/// <summary>Host-owned combat state and rules. Call only on the game thread of the authority.</summary>
public sealed class CombatSystem
{
    private readonly GameWorld _world;
    private readonly Guid _peerId;
    private readonly Func<NetworkMessage, Task> _publish;
    private readonly Action<Unit, Unit?> _notifyLoss;
    private double _hostTime;

    public CombatSystem(GameWorld world, Guid peerId, Func<NetworkMessage, Task> publish,
        Action<Unit, Unit?> notifyLoss)
    {
        _world = world;
        _peerId = peerId;
        _publish = publish;
        _notifyLoss = notifyLoss;
    }
    public int ActiveProjectileCount => _hostProjectiles.Count;
    public int PendingImpactCount => _projectileImpacts.Count;
    public void Reset()
    {
        _hostProjectiles.Clear();
        _projectileImpacts.Clear();
        _hostTime = 0;
    }
    public void Update(double hostTime, float elapsedSeconds, Action<NetworkMessage> enqueue)
    {
        _hostTime = hostTime;
        SimulateProjectiles(elapsedSeconds);
        UpdateDefensiveTargets();
        foreach (Unit unit in _world.Units.Units)
        {
            if (!unit.IsReadyToShoot(_hostTime))
                continue;
            if (!unit.TryQueueShot(_hostTime, out Unit? target) || target is null)
                continue;
            Vector3 aimPosition = target.Position + Vector3.Up * (target.Height * 0.5f);
            enqueue(NetworkCommands.CreateAttackRequest(
                _peerId,
                [unit.UnitId],
                aimPosition.X, aimPosition.Y, aimPosition.Z));
        }

        foreach (Unit unit in _world.Units.Units)
        {
            if (!unit.IsReadyToShoot(_hostTime))
                continue;
            if (!unit.TryQueueGroundShot(_hostTime, out Vector3 target))
                continue;
            enqueue(NetworkCommands.CreateAttackRequest(
                _peerId, [unit.UnitId], target.X, target.Y, target.Z));
        }
    }

    private readonly List<HostProjectile> _hostProjectiles = [];
    private readonly List<ProjectileImpact> _projectileImpacts = [];
    private sealed class HostProjectile(
        Guid projectileId,
        Guid attackerId,
        Vector3 start,
        Vector3 initialVelocity,
        ProjectileKind kind,
        float damage,
        DamageType damageType)
    {
        public Guid ProjectileId { get; } = projectileId;
        public Guid AttackerId { get; } = attackerId;
        public Vector3 Start { get; } = start;
        public Vector3 InitialVelocity { get; } = initialVelocity;
        public ProjectileKind Kind { get; } = kind;
        public float Damage { get; } = damage;
        public DamageType DamageType { get; } = damageType;
        public ProjectileFlightProfile Profile { get; } = ProjectileFlightProfile.For(kind);
        public float Age { get; set; }
    }

    private sealed record ProjectileImpact(
        Guid ProjectileId,
        Guid AttackerId,
        Vector3 Position,
        Vector3 Normal,
        Guid? HitUnitId,
        float Damage,
        DamageType DamageType,
        float ExplosionRadius);

    private void UpdateDefensiveTargets()
    {
        IReadOnlyList<Unit> units = _world.Units.GetSnapshot();
        foreach (Unit defender in units)
        {
            // Player-issued targets always win over automatic defense.
            if (defender.HasExplicitTarget || defender.Behavior == UnitBehavior.Passive)
            {
                if (defender.ClearTemporaryTarget())
                    PublishTemporaryTarget(defender, null);
                continue;
            }

            Unit? currentTarget = defender.TemporaryTargetUnitId is Guid currentTargetId
                ? _world.Units.FindById(currentTargetId)
                : null;
            if (currentTarget is not null &&
                defender.ShouldAttack(currentTarget) &&
                IsWithinAttackRange(defender, currentTarget))
            {
                continue;
            }

            Unit? newTarget = units
                .Where(candidate => defender.ShouldAttack(candidate))
                .Where(candidate => IsWithinAttackRange(defender, candidate))
                .OrderBy(candidate => HorizontalDistanceSquared(defender, candidate))
                .FirstOrDefault();

            if (newTarget is not null)
            {
                if (defender.SetTemporaryTarget(newTarget.UnitId))
                    PublishTemporaryTarget(defender, newTarget.UnitId);
            }
            else if (defender.ClearTemporaryTarget())
            {
                PublishTemporaryTarget(defender, null);
            }
        }
    }

    private static bool IsWithinAttackRange(Unit attacker, Unit target) =>
        HorizontalDistanceSquared(attacker, target) <= attacker.AttackRange * attacker.AttackRange;

    private static float HorizontalDistanceSquared(Unit first, Unit second)
    {
        float x = first.Position.X - second.Position.X;
        float z = first.Position.Z - second.Position.Z;
        return x * x + z * z;
    }

    private void PublishTemporaryTarget(Unit unit, Guid? targetId)
    {
        NetworkMessage command = NetworkCommands.CreateTemporaryTargetCommand(
            _peerId,
            unit.UnitId,
            targetId);
        _ = _publish(command);
    }

    public async Task ResolveAttackAsync(NetworkMessage request)
    {
        Vector3 impactPosition = new(request.X, request.Y, request.Z);

        foreach (Guid attackerId in request.UnitIds ?? Array.Empty<Guid>())
        {
            if (_world.Units.FindById(attackerId) is not Unit attacker || attacker.IsDying ||
                attacker.IsEmbarked || !attacker.CanFireWeapon)
                continue;

            if (attacker.ProjectileKind == ProjectileKind.Rocket)
            {
                Vector3 launchPosition = attacker.TryGetProjectileLaunchWorldTransform(out Matrix launchTransform)
                    ? launchTransform.Translation
                    : attacker.Position + Vector3.Up * (attacker.Height * 0.75f);
                ProjectileFlightProfile profile = ProjectileFlightProfile.For(ProjectileKind.Rocket);
                Vector3 launchDirection = CalculateRocketLaunchDirection(
                    launchPosition,
                    impactPosition,
                    attacker.ProjectileSpeed,
                    profile,
                    attacker.Transform.Forward);
                Vector3 initialVelocity = launchDirection * attacker.ProjectileSpeed;
                Guid projectileId = Guid.NewGuid();
                _hostProjectiles.Add(new HostProjectile(
                    projectileId,
                    attackerId,
                    launchPosition,
                    initialVelocity,
                    ProjectileKind.Rocket,
                    attacker.AttackDamage,
                    attacker.AttackDamageType));

                NetworkMessage spawnCommand = NetworkCommands.CreateProjectileSpawnCommand(
                    _peerId,
                    projectileId,
                    attackerId,
                    ProjectileKind.Rocket,
                    launchPosition,
                    initialVelocity,
                    _hostTime);
                await _publish(spawnCommand);
                continue;
            }

            if (attacker.UsesHitscanWeapon)
            {
                NetworkMessage impactCommand = NetworkCommands.CreateBulletImpactCommand(
                    _peerId, attackerId, impactPosition);
                await _publish(impactCommand);
            }

            await ApplyImpactDamageAsync(attackerId, impactPosition,
                attacker.AttackDamage, attacker.AttackDamageType);
        }
    }

    public void SimulateProjectiles(float elapsedSeconds)
    {
        for (int index = _hostProjectiles.Count - 1; index >= 0; index--)
        {
            HostProjectile projectile = _hostProjectiles[index];
            ProjectileTrajectory.Evaluate(
                projectile.Start,
                projectile.InitialVelocity,
                projectile.Age,
                projectile.Profile,
                out Vector3 start,
                out _);
            float nextAge = projectile.Age + elapsedSeconds;
            ProjectileTrajectory.Evaluate(
                projectile.Start,
                projectile.InitialVelocity,
                nextAge,
                projectile.Profile,
                out Vector3 end,
                out _);

            bool collided = TryFindProjectileCollision(
                projectile.AttackerId,
                start,
                end,
                out Vector3 impactPosition,
                out Vector3 impactNormal,
                out Guid? hitUnitId);
            if (collided || nextAge >= projectile.Profile.MaximumLifetime)
            {
                if (!collided)
                {
                    impactPosition = end;
                    impactNormal = Vector3.Up;
                }

                _projectileImpacts.Add(new ProjectileImpact(
                    projectile.ProjectileId,
                    projectile.AttackerId,
                    impactPosition,
                    impactNormal,
                    hitUnitId,
                    projectile.Damage,
                    projectile.DamageType,
                    projectile.Profile.ExplosionRadius));
                _hostProjectiles.RemoveAt(index);
                continue;
            }

            projectile.Age = nextAge;
        }
    }

    private static Vector3 CalculateRocketLaunchDirection(
        Vector3 start,
        Vector3 target,
        float initialSpeed,
        ProjectileFlightProfile profile,
        Vector3 fallbackDirection)
    {
        Vector2 horizontalOffset = new(target.X - start.X, target.Z - start.Z);
        float averagePoweredSpeed = Math.Max(
            0.1f,
            initialSpeed + profile.MotorAcceleration * profile.MotorBurnDuration * 0.5f);
        float estimatedFlightTime = horizontalOffset.Length() / averagePoweredSpeed;
        Vector3 compensatedTarget = target + Vector3.Up *
            (0.5f * profile.Gravity * estimatedFlightTime * estimatedFlightTime);
        Vector3 direction = compensatedTarget - start;
        if (direction.LengthSquared() > 0.0001f)
            return Vector3.Normalize(direction);

        fallbackDirection.Y = 0.0f;
        return fallbackDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(fallbackDirection)
            : Vector3.Forward;
    }

    public async Task PublishImpactsAsync()
    {
        if (_projectileImpacts.Count == 0)
            return;

        ProjectileImpact[] impacts = [.. _projectileImpacts];
        _projectileImpacts.Clear();
        foreach (ProjectileImpact impact in impacts)
        {
            NetworkMessage impactCommand = NetworkCommands.CreateProjectileImpactCommand(
                _peerId,
                impact.ProjectileId,
                impact.AttackerId,
                impact.Position,
                impact.Normal,
                impact.HitUnitId,
                _hostTime);
            await _publish(impactCommand);
            await ApplyExplosionDamageAsync(impact);
        }
    }

    private bool TryFindProjectileCollision(
        Guid attackerId,
        Vector3 start,
        Vector3 end,
        out Vector3 position,
        out Vector3 normal,
        out Guid? hitUnitId)
    {
        float closestT = float.MaxValue;
        position = default;
        normal = Vector3.Up;
        hitUnitId = null;

        Vector3 segment = end - start;
        float distance = segment.Length();
        int samples = Math.Max(1, (int)MathF.Ceiling(distance / 0.2f));
        for (int sample = 1; sample <= samples; sample++)
        {
            float t = (float)sample / samples;
            Vector3 point = Vector3.Lerp(start, end, t);
            int terrainX = (int)MathF.Floor(point.X);
            int terrainZ = (int)MathF.Floor(point.Z);
            if (terrainX < 0 || terrainZ < 0 ||
                terrainX >= _world.Terrain.Width || terrainZ >= _world.Terrain.Height)
                continue;
            float terrainHeight = _world.Terrain.GetHeight(terrainX, terrainZ);
            if (point.Y > terrainHeight + 0.03f)
                continue;
            closestT = t;
            position = new Vector3(point.X, terrainHeight, point.Z);
            break;
        }

        foreach (Unit unit in _world.Units.Units)
        {
            if (unit.UnitId == attackerId || !unit.CanBeTargeted)
                continue;

            float footprintRadius = Math.Max(unit.Width, unit.Length) *
                _world.GameGrid.CellSize * 0.45f;
            float radius = Math.Max(0.35f, Math.Min(footprintRadius, unit.Height * 0.65f));
            Vector3 center = unit.Position + Vector3.Up * (unit.Height * 0.5f);
            if (!TrySegmentSphere(start, end, center, radius, out float t) || t >= closestT)
                continue;

            closestT = t;
            position = Vector3.Lerp(start, end, t);
            normal = position - center;
            normal = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.Up;
            hitUnitId = unit.UnitId;
        }

        return closestT != float.MaxValue;
    }

    private static bool TrySegmentSphere(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float radius,
        out float t)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            t = 0.0f;
            return Vector3.DistanceSquared(start, center) <= radius * radius;
        }

        t = MathHelper.Clamp(Vector3.Dot(center - start, segment) / lengthSquared, 0.0f, 1.0f);
        return Vector3.DistanceSquared(Vector3.Lerp(start, end, t), center) <= radius * radius;
    }

    private async Task ApplyExplosionDamageAsync(ProjectileImpact impact)
    {
        Unit[] targets = _world.Units.Units
            .Where(unit => unit.CanBeTargeted)
            .Where(unit =>
            {
                float x = unit.Position.X - impact.Position.X;
                float z = unit.Position.Z - impact.Position.Z;
                return x * x + z * z <= impact.ExplosionRadius * impact.ExplosionRadius;
            })
            .ToArray();

        foreach (Unit target in targets)
        {
            float horizontalDistance = Vector2.Distance(
                new Vector2(target.Position.X, target.Position.Z),
                new Vector2(impact.Position.X, impact.Position.Z));
            float damageFactor = MathHelper.Lerp(
                1.0f,
                0.25f,
                MathHelper.Clamp(horizontalDistance / impact.ExplosionRadius, 0.0f, 1.0f));
            await ApplyDamageAsync(target, impact.AttackerId, impact.Position,
                impact.Damage * damageFactor, impact.DamageType);
        }
    }

    public Unit? FindImpactTarget(Guid attackerId, Vector3 impactPosition)
    {
        const float attackRadius = 1.5f;
        return _world.Units.Units.Where(unit =>
        {
            if (unit.UnitId == attackerId || !unit.CanBeTargeted) return false;
            float bottom = unit.Position.Y - (unit is Helicopter helicopter ? helicopter.GroundOffset : 0);
            if (impactPosition.Y < bottom - 0.25f || impactPosition.Y > bottom + unit.Height + 0.25f) return false;
            Vector2 offset = new(unit.Position.X - impactPosition.X, unit.Position.Z - impactPosition.Z);
            return offset.LengthSquared() <= attackRadius * attackRadius;
        }).OrderBy(unit => Vector3.DistanceSquared(unit.Position + Vector3.Up * unit.Height * 0.5f, impactPosition)).FirstOrDefault();
    }

    private async Task ApplyImpactDamageAsync(Guid attackerId, Vector3 impactPosition,
        float baseDamage, DamageType damageType)
    {
        Unit? target = FindImpactTarget(attackerId, impactPosition);

        if (target is null)
            return;

        await ApplyDamageAsync(target, attackerId, impactPosition, baseDamage, damageType);
    }

    private async Task ApplyDamageAsync(Unit target, Guid attackerId, Vector3 impactPosition,
        float baseDamage, DamageType damageType)
    {
        // Absolute HP replication never invokes OnHit a second time on either peer.
        if (!target.CanBeTargeted || target.HitPoints <= 0) return;
        float damage = DamageCalculator.Calculate(baseDamage, damageType, target.Armor);
        damage = SquadBenefits.ApplyCombatModifiers(
            _world, _world.Units.FindById(attackerId), target, damage);
        HitInfo hit = new(attackerId, impactPosition, damage);
        bool destroyed = target.OnHit(hit);
        NetworkMessage hitCommand = NetworkCommands.CreateUnitHitCommand(
            _peerId,
            target.UnitId,
            attackerId,
            impactPosition,
            damage,
            target.HitPoints);
        await _publish(hitCommand);

        if (!destroyed)
            return;

        _notifyLoss(target, _world.Units.FindById(attackerId));
        _world.Units.Destroy(target.UnitId);
        NetworkMessage destroyCommand = NetworkCommands.CreateDestroyUnitCommand(
            _peerId,
            target.UnitId);
        await _publish(destroyCommand);
    }
}
