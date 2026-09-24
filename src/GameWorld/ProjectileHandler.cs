using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class ProjectileHandler
{
    private readonly List<Projectile> _projectiles = [];
    private readonly List<PendingVisualImpact> _pendingImpacts = [];
    public int ActiveProjectileCount => _projectiles.Count + _pendingImpacts.Count;

    public void Clear()
    {
        _projectiles.Clear();
        _pendingImpacts.Clear();
    }

    private sealed class PendingVisualImpact(Guid projectileId, Vector3 position)
    {
        public Guid ProjectileId { get; } = projectileId;
        public Vector3 Position { get; } = position;
        public float RemainingSeconds { get; set; } = Projectile.ReplicatedRenderDelay;
    }

    public void Fire(
        Vector3 start,
        Vector3 target,
        ProjectileKind kind = ProjectileKind.BallisticShell,
        float speed = 35.0f)
    {
        _projectiles.Add(new Projectile(start, target, kind, speed));
    }

    public void SpawnReplicated(
        Guid projectileId,
        Vector3 start,
        Vector3 initialVelocity,
        ProjectileKind kind)
    {
        if (_projectiles.Exists(projectile => projectile.ProjectileId == projectileId))
            return;
        _projectiles.Add(new Projectile(projectileId, start, initialVelocity, kind));
    }

    public void ApplyAuthoritativeImpact(Guid projectileId, Vector3 position)
    {
        int index = _projectiles.FindIndex(projectile => projectile.ProjectileId == projectileId);
        if (index < 0)
        {
            Globals.World.Particles.EmitExplosion(position);
            return;
        }
        if (!_pendingImpacts.Exists(impact => impact.ProjectileId == projectileId))
            _pendingImpacts.Add(new PendingVisualImpact(projectileId, position));
    }

    public void Update(GameTime gameTime)
    {
        float elapsedSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        for (int index = _projectiles.Count - 1; index >= 0; index--)
        {
            Projectile projectile = _projectiles[index];
            projectile.Update(gameTime);
            if (projectile.IsExpired)
            {
                if (projectile.ExplodeOnExpiry)
                    Globals.World.Particles.EmitExplosion(projectile.Position);
                _projectiles.RemoveAt(index);
            }
        }

        for (int index = _pendingImpacts.Count - 1; index >= 0; index--)
        {
            PendingVisualImpact impact = _pendingImpacts[index];
            impact.RemainingSeconds -= elapsedSeconds;
            if (impact.RemainingSeconds > 0.0f)
                continue;

            _projectiles.RemoveAll(projectile => projectile.ProjectileId == impact.ProjectileId);
            Globals.World.Particles.EmitExplosion(impact.Position);
            _pendingImpacts.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        foreach (Projectile projectile in _projectiles)
            projectile.Draw(effect);
    }

    public void DrawShadow(Effect effect)
    {
        foreach (Projectile projectile in _projectiles)
            projectile.DrawShadow(effect);
    }
}
