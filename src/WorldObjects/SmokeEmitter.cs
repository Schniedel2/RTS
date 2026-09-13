using Microsoft.Xna.Framework;
using System;

namespace RTS;

/// <summary>
/// A non-selectable visual world object that continuously emits textured smoke.
/// A null lifetime keeps it alive until its handler removes it explicitly.
/// </summary>
public sealed class SmokeEmitter : WorldObject
{
    private float _elapsed;
    private float _emissionAccumulator;

    public SmokeEmissionSettings Settings { get; set; }
    public Vector3 Direction { get; set; }
    /// <summary>Number of smoke bursts per second. Each burst uses Settings.ParticleCount.</summary>
    public float EmissionsPerSecond { get; set; }
    public float? Lifetime { get; }
    public bool IsExpired => Lifetime is float lifetime && _elapsed >= lifetime;

    public SmokeEmitter(
        Vector3 position,
        SmokeEmissionSettings settings,
        float emissionsPerSecond = 2.0f,
        float? lifetime = null,
        Vector3? direction = null) : base(position)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        EmissionsPerSecond = Math.Max(0.0f, emissionsPerSecond);
        if (lifetime is not null && lifetime <= 0.0f)
            lifetime = null;
        Lifetime = lifetime is float value ? Math.Max(0.0f, value) : null;        
        Direction = direction ?? Vector3.Up;
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += deltaSeconds;
        if (IsExpired || EmissionsPerSecond <= 0.0f)
            return;

        _emissionAccumulator += deltaSeconds * EmissionsPerSecond;
        int burstCount = Math.Min(8, (int)_emissionAccumulator);
        _emissionAccumulator -= burstCount;
        for (int index = 0; index < burstCount; index++)
            Globals.World.Particles.EmitSmoke(Position, Direction, Settings);
    }
}
