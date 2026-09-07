using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public sealed class ParticleSystem
{
    private readonly List<Particle> _particles = [];
    private const int MaximumParticles = 512;

    public void EmitRocketTrail(Vector3 position)
    {
        if (_particles.Count >= MaximumParticles)
            return;

        Vector3 velocity = new(
            Random.Shared.NextSingle() - 0.5f,
            Random.Shared.NextSingle() * 0.5f,
            Random.Shared.NextSingle() - 0.5f);
        _particles.Add(new Particle(position, velocity, Color.Orange, 0.25f, 0.18f));
    }

    public void EmitExplosion(Vector3 position)
    {
        const int particleCount = 28;
        for (int index = 0; index < particleCount && _particles.Count < MaximumParticles; index++)
        {
            float angle = Random.Shared.NextSingle() * MathHelper.TwoPi;
            float speed = 2.5f + Random.Shared.NextSingle() * 5.0f;
            Vector3 velocity = new(
                MathF.Cos(angle) * speed,
                1.0f + Random.Shared.NextSingle() * 4.0f,
                MathF.Sin(angle) * speed);
            Color color = index % 3 == 0 ? Color.Yellow : Color.OrangeRed;
            _particles.Add(new Particle(
                position + Vector3.Up * 0.1f,
                velocity,
                color,
                0.25f + Random.Shared.NextSingle() * 0.35f,
                0.35f + Random.Shared.NextSingle() * 0.35f));
        }
    }

    public void Update(GameTime gameTime)
    {
        for (int index = _particles.Count - 1; index >= 0; index--)
        {
            Particle particle = _particles[index];
            particle.Update(gameTime);
            if (particle.IsExpired)
                _particles.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        foreach (Particle particle in _particles)
            particle.Draw(effect);
    }
}
