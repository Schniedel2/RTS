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
    private readonly List<DebrisParticle> _debrisParticles = [];
    /// <summary>
    /// Shared visual-particle budget. 2048 gives impacts and muzzle effects
    /// enough headroom during a small battle while still providing a hard
    /// upper bound until the renderer batches billboard draws.
    /// </summary>
    public int MaximumParticles { get; set; } = 2048;

    public int ActiveParticleCount =>
        _particles.Count + _smokeParticles.Count + _debrisParticles.Count;

    private bool HasParticleCapacity => ActiveParticleCount < MaximumParticles;

    public void EmitRocketTrail(Vector3 position)
    {
        if (!HasParticleCapacity)
            return;

        Vector3 velocity = new(
            Random.Shared.NextSingle() - 0.5f,
            Random.Shared.NextSingle() * 0.5f,
            Random.Shared.NextSingle() - 0.5f);
        _particles.Add(new Particle(position, velocity, Color.Orange, 0.25f, 0.18f, windInfluence: 0.25f));
    }

    public void EmitExplosion(Vector3 position, ExplosionEmissionSettings? settings = null)
    {
        settings ??= ExplosionEmissionPresets.TankShell();
        Globals.World.Decals.AddScorchMark(position, 1.9f * settings.Intensity);
        Globals.World.Weather.AddExplosionWind(
            position,
            settings.ShockwaveRadius,
            settings.ShockwaveStrength * settings.Intensity,
            settings.ShockwaveLifetime);
        bool hasExplosionSprites = Globals.TilemapHandler.TryGet(
            settings.ExplosionTilemapName, out TilemapHandler.Tilemap explosionTilemap);
        if (hasExplosionSprites)
        {
            for (int index = 0; index < settings.FireParticleCount && HasParticleCapacity; index++)
            {
                float angle = Random.Shared.NextSingle() * MathHelper.TwoPi;
                Vector3 velocity = new(
                    MathF.Cos(angle) * (1.0f + Random.Shared.NextSingle() * 2.5f),
                    1.0f + Random.Shared.NextSingle() * 3.0f,
                    MathF.Sin(angle) * (1.0f + Random.Shared.NextSingle() * 2.5f));
                Color color = index % 3 == 0 ? Color.Yellow : Color.OrangeRed;
                _smokeParticles.Add(new SmokeParticle(
                    position + Vector3.Up * 0.12f,
                    velocity,
                    explosionTilemap,
                    Random.Shared.Next(explosionTilemap.TileCount),
                    (0.30f + Random.Shared.NextSingle() * 0.20f) * settings.Intensity,
                    (0.85f + Random.Shared.NextSingle() * 0.65f) * settings.Intensity,
                    0.28f + Random.Shared.NextSingle() * 0.22f,
                    Random.Shared.NextSingle() * MathHelper.TwoPi,
                    (Random.Shared.NextSingle() - 0.5f) * 4.0f,
                    color,
                    0.90f,
                    0.10f));
            }
        }
        else for (int index = 0; index < settings.FireParticleCount && HasParticleCapacity; index++)
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
                0.35f + Random.Shared.NextSingle() * 0.35f,
                windInfluence: 0.35f));
        }

        Globals.TilemapHandler.TryGet(settings.SparksTilemapName, out TilemapHandler.Tilemap sparkTilemap);
        for (int index = 0; index < settings.DebrisCount && HasParticleCapacity; index++)
        {
            float angle = Random.Shared.NextSingle() * MathHelper.TwoPi;
            float speed = settings.DebrisSpeed + (Random.Shared.NextSingle() * 2.0f - 1.0f) * settings.DebrisSpeedVariation;
            Vector3 velocity = new(
                MathF.Cos(angle) * speed,
                1.5f + Random.Shared.NextSingle() * 4.0f,
                MathF.Sin(angle) * speed);
            _debrisParticles.Add(new DebrisParticle(
                position + Vector3.Up * 0.12f,
                velocity,
                settings.DebrisLifetime,
                settings.DebrisSmokeInterval,
                settings.DebrisSmokeSettings,
                sparkTilemap));
        }
    }

    /// <summary>Creates a short, expanding muzzle-smoke plume from the registered Smoke tilemap.</summary>
    public void EmitCannonSmoke(
        Vector3 position,
        Vector3 barrelDirection,
        SmokeEmissionSettings? settings = null)
    {
        EmitSmoke(position, barrelDirection, settings ?? SmokeEmissionPresets.TankCannon());
    }

    /// <summary>Emits a configurable smoke burst in an arbitrary world direction.</summary>
    public void EmitSmoke(
        Vector3 position,
        Vector3 direction,
        SmokeEmissionSettings settings)
    {
        if (!Globals.TilemapHandler.TryGet(settings.TilemapName, out TilemapHandler.Tilemap smoke))
            return;

        direction = direction.LengthSquared() > 0.0001f
            ? Vector3.Normalize(direction)
            : Vector3.Forward;
        Vector3 sideways = Vector3.Cross(direction, Vector3.Up);
        sideways = sideways.LengthSquared() < 0.0001f ? Vector3.Right : Vector3.Normalize(sideways);

        int particleCount = Math.Max(0, settings.ParticleCount);
        for (int index = 0; index < particleCount && HasParticleCapacity; index++)
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
                settings.Opacity * settings.Intensity,
                settings.WindInfluence));
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
        for (int index = _debrisParticles.Count - 1; index >= 0; index--)
        {
            DebrisParticle particle = _debrisParticles[index];
            particle.Update(gameTime);
            if (particle.IsExpired)
                _debrisParticles.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        foreach (Particle particle in _particles)
            particle.Draw(effect);
        foreach (DebrisParticle particle in _debrisParticles.Where(particle => !particle.UsesSprite))
            particle.Draw(effect);

        if (_smokeParticles.Count == 0 && !_debrisParticles.Any(particle => particle.UsesSprite))
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
        effect.Parameters["PlayerSkinStrength"]?.SetValue(0.0f);
        effect.Parameters["Unlit"]?.SetValue(1.0f);
        try
        {
            foreach (DebrisParticle particle in _debrisParticles.Where(particle => particle.UsesSprite).OrderByDescending(particle =>
                Vector3.DistanceSquared(particle.Position, Globals._camera.Position)))
            {
                particle.Draw(effect);
            }
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
