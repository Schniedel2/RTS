using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public sealed class ParticleSystem
{
    private readonly List<Particle> _particles = [];
    private readonly List<SmokeParticle> _smokeParticles = [];
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

    /// <summary>Creates a short, expanding muzzle-smoke plume from the registered Smoke tilemap.</summary>
    public void EmitCannonSmoke(
        Vector3 position,
        Vector3 barrelDirection,
        SmokeEmissionSettings? settings = null)
    {
        settings ??= SmokeEmissionPresets.TankCannon();
        if (!Globals.TilemapHandler.TryGet(settings.TilemapName, out TilemapHandler.Tilemap smoke))
            return;

        Vector3 direction = barrelDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(barrelDirection)
            : Vector3.Forward;
        Vector3 sideways = Vector3.Cross(direction, Vector3.Up);
        sideways = sideways.LengthSquared() < 0.0001f ? Vector3.Right : Vector3.Normalize(sideways);

        int particleCount = Math.Max(0, settings.ParticleCount);
        for (int index = 0; index < particleCount && _smokeParticles.Count + _particles.Count < MaximumParticles; index++)
        {
            Vector3 velocity = direction * RandomRange(settings.ForwardSpeed, settings.ForwardSpeedVariation) +
                sideways * ((Random.Shared.NextSingle() - 0.5f) * settings.SidewaysSpread) +
                Vector3.Up * RandomRange(settings.UpwardSpeed, settings.UpwardSpeedVariation);
            _smokeParticles.Add(new SmokeParticle(
                position + direction * (Random.Shared.NextSingle() * settings.EmissionLength),
                velocity,
                smoke,
                Random.Shared.Next(smoke.TileCount),
                MathF.Max(0.01f, RandomRange(settings.StartSize, settings.StartSizeVariation) * settings.Intensity),
                MathF.Max(0.01f, RandomRange(settings.EndSize, settings.EndSizeVariation) * settings.Intensity),
                MathF.Max(0.01f, RandomRange(settings.Lifetime, settings.LifetimeVariation)),
                Random.Shared.NextSingle() * MathHelper.TwoPi,
                (Random.Shared.NextSingle() - 0.5f) * 1.8f,
                settings.Color,
                settings.Opacity * settings.Intensity));
        }
    }

    private static float RandomRange(float center, float variation) =>
        center + (Random.Shared.NextSingle() * 2.0f - 1.0f) * variation;

    public void Update(GameTime gameTime)
    {
        for (int index = _particles.Count - 1; index >= 0; index--)
        {
            Particle particle = _particles[index];
            particle.Update(gameTime);
            if (particle.IsExpired)
                _particles.RemoveAt(index);
        }
        for (int index = _smokeParticles.Count - 1; index >= 0; index--)
        {
            SmokeParticle particle = _smokeParticles[index];
            particle.Update(gameTime);
            if (particle.IsExpired)
                _smokeParticles.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        foreach (Particle particle in _particles)
            particle.Draw(effect);

        if (_smokeParticles.Count == 0)
            return;

        GraphicsDevice graphicsDevice = Globals.GraphicsDevice;
        BlendState previousBlendState = graphicsDevice.BlendState;
        DepthStencilState previousDepthState = graphicsDevice.DepthStencilState;
        RasterizerState previousRasterizerState = graphicsDevice.RasterizerState;
        // PNG tilemaps normally contain straight (non-premultiplied) alpha.
        // Billboards must be two-sided because their face orientation changes
        // with the camera.
        graphicsDevice.BlendState = BlendState.NonPremultiplied;
        graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.Parameters["PlayerColorStrength"]?.SetValue(0.0f);
        effect.Parameters["Unlit"]?.SetValue(1.0f);
        try
        {
            foreach (SmokeParticle particle in _smokeParticles.OrderByDescending(particle =>
                Vector3.DistanceSquared(particle.Position, Globals._camera.Position)))
            {
                particle.Draw(effect);
            }
        }
        finally
        {
            graphicsDevice.BlendState = previousBlendState;
            graphicsDevice.DepthStencilState = previousDepthState;
            graphicsDevice.RasterizerState = previousRasterizerState;
            effect.Parameters["Unlit"]?.SetValue(0.0f);
        }
    }
}
