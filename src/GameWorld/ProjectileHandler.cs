using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace RTS;

public sealed class ProjectileHandler
{
    private readonly List<Projectile> _projectiles = [];

    public void Fire(
        Vector3 start,
        Vector3 target,
        ProjectileKind kind = ProjectileKind.BallisticShell,
        float speed = 35.0f)
    {
        _projectiles.Add(new Projectile(start, target, kind, speed));
    }

    public void Update(GameTime gameTime)
    {
        for (int index = _projectiles.Count - 1; index >= 0; index--)
        {
            Projectile projectile = _projectiles[index];
            projectile.Update(gameTime);
            if (projectile.IsExpired)
            {
                Globals.World.Particles.EmitExplosion(projectile.Position);
                _projectiles.RemoveAt(index);
            }
        }
    }

    public void Draw(Effect effect)
    {
        foreach (Projectile projectile in _projectiles)
            projectile.Draw(effect);
    }
}
