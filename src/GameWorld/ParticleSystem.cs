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

    /// <summary>
    /// World-space distance between consecutive rocket-trail emission points.
    /// Because emission is distance based, trail density is independent of
    /// frame rate and projectile speed.
    /// </summary>
    public float RocketTrailSpacing { get; set; } = 0.1f;

    public int ActiveParticleCount =>
        _particles.Count + _smokeParticles.Count + _debrisParticles.Count;

    private bool HasParticleCapacity => ActiveParticleCount < MaximumParticles;

    public void Clear()
    {
        _particles.Clear();
        _smokeParticles.Clear();
        _debrisParticles.Clear();
    }

    public void EmitRocketTrail(
        Vector3 position,
        Vector3 direction,
        float exhaustStrength,
        bool motorActive)
    {
        if (!HasParticleCapacity)
            return;

        exhaustStrength = MathHelper.Clamp(exhaustStrength, 0.0f, 1.0f);
        bool emitSmoke = exhaustStrength > 0.015f;
        if (emitSmoke)
            EmitSmoke(position, direction, SmokeEmissionPresets.RocketTrail(exhaustStrength));

        // Residual smoke eases out after burnout, but the hot core exists only
        // while the motor is physically producing thrust.
        if (!motorActive)
            return;

        Vector3 velocity = new(
            Random.Shared.NextSingle() - 0.5f,
            Random.Shared.NextSingle() * 0.5f,
            Random.Shared.NextSingle() - 0.5f);
        _particles.Add(new Particle(
            position,
            velocity,
            emitSmoke ? Color.OrangeRed : Color.Orange,
            emitSmoke ? 0.10f + 0.08f * exhaustStrength : 0.25f,
            emitSmoke ? 0.06f + 0.05f * exhaustStrength : 0.18f,
            windInfluence: emitSmoke ? 0.0f : 0.25f));
    }

    public void EmitExplosion(Vector3 position, ExplosionEmissionSettings? settings = null)
    {
        settings ??= ExplosionEmissionPresets.TankShell();
        if (settings.CreateScorchMark)
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

        for (int index = 0; index < settings.SmokeBurstCount && HasParticleCapacity; index++)
        {
            float angle = Random.Shared.NextSingle() * MathHelper.TwoPi;
            float radius = Random.Shared.NextSingle() * 0.75f * settings.Intensity;
            Vector3 smokePosition = position + new Vector3(
                MathF.Cos(angle) * radius,
                Random.Shared.NextSingle() * 0.65f * settings.Intensity,
                MathF.Sin(angle) * radius);
            EmitSmoke(smokePosition, Vector3.Up, settings.SmokeBurstSettings);
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

    /// <summary>
    /// Creates a brief, directed small-arms muzzle flash. Unlike an impact
    /// explosion it does not rise, create a decal, disturb local wind or use
    /// the global explosion particle budget for debris.
    /// </summary>
    public void EmitRifleMuzzleFlash(
        Vector3 position,
        Vector3 barrelDirection,
        MuzzleFlashEmissionSettings? settings = null)
    {
        settings ??= MuzzleFlashEmissionPresets.Rifle();
        EmitMuzzleFlash(position, barrelDirection, settings);
    }

    /// <summary>Creates a large, short-lived cannon flash and its smoke plume.</summary>
    public void EmitCannonMuzzleFlash(
        Vector3 position,
        Vector3 barrelDirection,
        MuzzleFlashEmissionSettings? settings = null)
    {
        settings ??= MuzzleFlashEmissionPresets.TankCannon();
        EmitMuzzleFlash(position, barrelDirection, settings);
    }

    private void EmitMuzzleFlash(
        Vector3 position,
        Vector3 barrelDirection,
        MuzzleFlashEmissionSettings settings)
    {
        EmitSmoke(position, barrelDirection, settings.SmokeSettings);

        if (!Globals.TilemapHandler.TryGet(settings.TilemapName, out TilemapHandler.Tilemap flashes))
            return;

        Vector3 direction = barrelDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(barrelDirection)
            : Vector3.Forward;
        Vector3 sideways = Vector3.Cross(direction, Vector3.Up);
        sideways = sideways.LengthSquared() > 0.0001f ? Vector3.Normalize(sideways) : Vector3.Right;

        // Bright central flash at the barrel end. SmokeParticle provides the
        // required billboard and start-to-end-size interpolation; wind is
        // explicitly zero so a muzzle flash always follows the weapon.
        if (HasParticleCapacity)
        {
            _smokeParticles.Add(new SmokeParticle(
                position + direction * 0.015f,
                direction * 0.10f,
                flashes,
                Random.Shared.Next(flashes.TileCount),
                settings.MainStartSize,
                settings.MainEndSize,
                settings.Lifetime,
                Random.Shared.NextSingle() * MathHelper.TwoPi,
                0.0f,
                settings.Color,
                1.0f,
                windInfluence: 0.0f,
                buoyancy: 0.0f));
        }

        for (int index = 0; index < settings.SecondaryFlashCount && HasParticleCapacity; index++)
        {
            float distance = settings.SecondaryDistance * (0.45f + Random.Shared.NextSingle() * 0.80f);
            Vector3 offset = direction * distance +
                sideways * ((Random.Shared.NextSingle() - 0.5f) * 0.09f) +
                Vector3.Up * ((Random.Shared.NextSingle() - 0.5f) * 0.07f);
            _smokeParticles.Add(new SmokeParticle(
                position + offset,
                direction * (settings.SecondarySpeed * (0.75f + Random.Shared.NextSingle() * 0.45f)),
                flashes,
                Random.Shared.Next(flashes.TileCount),
                settings.SecondaryStartSize * (0.75f + Random.Shared.NextSingle() * 0.35f),
                settings.SecondaryEndSize,
                settings.Lifetime * (0.70f + Random.Shared.NextSingle() * 0.25f),
                Random.Shared.NextSingle() * MathHelper.TwoPi,
                0.0f,
                settings.Color,
                0.82f,
                windInfluence: 0.0f,
                buoyancy: 0.0f));
        }
    }

    /// <summary>
    /// Renders the small, local visual impact of a hitscan bullet. The host
    /// replicates the position; the surface-dependent presentation stays local
    /// and can be refined without affecting simulation or networking.
    /// </summary>
    public void EmitBulletImpact(Vector3 impactPosition)
    {
        Terrain terrain = Globals.World.Terrain;
        int x = Math.Clamp((int)MathF.Floor(impactPosition.X), 0, terrain.Width - 1);
        int z = Math.Clamp((int)MathF.Floor(impactPosition.Z), 0, terrain.Height - 1);
        impactPosition.Y = terrain.GetHeight(x, z) + 0.025f;

        TerrainTile surface = terrain.GetTile(x, z);
        Color dustColor = surface switch
        {
            TerrainTile.Sand => new Color(208, 180, 116),
            TerrainTile.Dirt => new Color(121, 90, 57),
            TerrainTile.Grass => new Color(102, 119, 65),
            TerrainTile.Stones or TerrainTile.Rock => new Color(145, 145, 140),
            _ => Color.LightGray
        };

        // Tiny solid fragments make a bullet strike readable even at a
        // distance. Their tint follows the surface, they have no wind or
        // buoyancy, and disappear before they can look like an explosion.
        if (Globals.TilemapHandler.TryGet("Sparks", out TilemapHandler.Tilemap fragments))
        {
            for (int index = 0; index < 4 && HasParticleCapacity; index++)
            {
                float angle = Random.Shared.NextSingle() * MathHelper.TwoPi;
                Vector3 direction = new(MathF.Cos(angle), 0.0f, MathF.Sin(angle));
                _smokeParticles.Add(new SmokeParticle(
                    position: impactPosition + Vector3.Up * 0.025f,
                    velocity: direction * (0.65f + Random.Shared.NextSingle() * 0.65f) + Vector3.Up * (0.35f + Random.Shared.NextSingle() * 0.25f),
                    tilemap: fragments,
                    tileIndex: Random.Shared.Next(fragments.TileCount),
                    startSize: 0.395f + Random.Shared.NextSingle() * 0.045f,
                    endSize: 0.012f,
                    lifetime: 0.12f + Random.Shared.NextSingle() * 0.07f,
                    rotationRadians: Random.Shared.NextSingle() * MathHelper.TwoPi,
                    rotationSpeedRadians: 0.0f,
                    color: dustColor,
                    opacity: 0.95f,
                    windInfluence: 0.0f,
                    buoyancy: 0.0f));
            }
        }

        // A couple of extremely short, tinted dust billboards form the base
        // effect. They may drift a little after the initial impact, unlike the
        // muzzle flash, because they represent loose surface material.
        EmitSmoke(impactPosition, Vector3.Up, new SmokeEmissionSettings
        {
            ParticleCount = 2,
            Intensity = 0.55f,
            EmissionLength = 0.08f,
            ForwardSpeed = 0.20f,
            ForwardSpeedVariation = 0.12f,
            SidewaysSpread = 0.32f,
            UpwardSpeed = 0.45f,
            UpwardSpeedVariation = 0.15f,
            StartSize = 0.07f,
            EndSize = 0.18f,
            Lifetime = 0.22f,
            LifetimeVariation = 0.06f,
            Color = dustColor,
            Opacity = 0.58f,
            WindInfluence = 0.20f
        });
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
                settings.WindInfluence,
                startDelay: MathF.Max(0.0f,
                    RandomRange(settings.StartDelay, settings.StartDelayVariation))));
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
        Player? localPlayer = Globals.Game.Players.FirstOrDefault(
            player => player.Id == Globals.Game.Network.LocalPeerId);
        bool revealAll = !Globals.FogOfWarEnabled || Globals.World.IsEditorActive || localPlayer is null;
        bool IsVisible(WorldObject particle) => revealAll ||
            Globals.World.Visibility.IsTerrainCurrentlyVisible(
                localPlayer!.ArmyId, Globals.World.GameGrid.ToCell(particle.Position));

        IEnumerable<Particle> visibleParticles = _particles.Where(IsVisible);
        IEnumerable<DebrisParticle> visibleDebris = _debrisParticles.Where(IsVisible);
        IEnumerable<SmokeParticle> visibleSmoke = _smokeParticles.Where(IsVisible);

        foreach (Particle particle in visibleParticles)
            particle.Draw(effect);
        foreach (DebrisParticle particle in visibleDebris.Where(particle => !particle.UsesSprite))
            particle.Draw(effect);

        if (!visibleSmoke.Any() && !visibleDebris.Any(particle => particle.UsesSprite))
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
            foreach (DebrisParticle particle in visibleDebris.Where(particle => particle.UsesSprite).OrderByDescending(particle =>
                Vector3.DistanceSquared(particle.Position, Globals._camera.Position)))
            {
                particle.Draw(effect);
            }
            foreach (SmokeParticle particle in visibleSmoke.OrderByDescending(particle =>
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
