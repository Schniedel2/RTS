using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>Configuration for one textured smoke emission.</summary>
public sealed record SmokeEmissionSettings
{
    public string TilemapName { get; init; } = "Smoke";
    public int ParticleCount { get; init; } = 5;
    /// <summary>Multiplies emitted size and opacity.</summary>
    public float Intensity { get; init; } = 1.0f;
    /// <summary>Length along the barrel direction over which particles are spawned.</summary>
    public float EmissionLength { get; init; } = 0.12f;
    public float ForwardSpeed { get; init; } = 1.1f;
    public float ForwardSpeedVariation { get; init; } = 0.45f;
    public float SidewaysSpread { get; init; } = 0.55f;
    public float UpwardSpeed { get; init; } = 0.45f;
    public float UpwardSpeedVariation { get; init; } = 0.40f;
    public float StartSize { get; init; } = 0.26f;
    public float StartSizeVariation { get; init; } = 0.10f;
    public float EndSize { get; init; } = 0.95f;
    public float EndSizeVariation { get; init; } = 0.35f;
    public float Lifetime { get; init; } = 0.70f;
    public float LifetimeVariation { get; init; } = 0.20f;
    public Color Color { get; init; } = new(184, 184, 184);
    public float Opacity { get; init; } = 0.62f;
    /// <summary>0 ignores wind; 1 drifts with the complete world wind velocity.</summary>
    public float WindInfluence { get; init; } = 0.85f;
}

/// <summary>Factories return new settings so callers can safely customize them.</summary>
public static class SmokeEmissionPresets
{
    public static SmokeEmissionSettings LightCannon() => new()
    {
        ParticleCount = 3,
        Intensity = 0.75f,
        EmissionLength = 0.08f,
        ForwardSpeed = 0.8f,
        StartSize = 0.16f,
        EndSize = 0.58f,
        Lifetime = 0.48f,
        Opacity = 0.45f
    };

    public static SmokeEmissionSettings TankCannon() => new()
    {
        ParticleCount = 12,
        Intensity = 1.15f,
        EmissionLength = 1.28f,
        ForwardSpeed = 1.35f,
        SidewaysSpread = 0.60f,
        UpwardSpeed = 0.52f,
        StartSize = 0.25f,
        EndSize = 1.10f,
        Lifetime = 1.78f,
        Opacity = 0.68f
    };

    public static SmokeEmissionSettings HeavyCannon() => new()
    {
        ParticleCount = 10,
        Intensity = 1.45f,
        EmissionLength = 0.42f,
        ForwardSpeed = 1.8f,
        SidewaysSpread = 0.85f,
        UpwardSpeed = 0.65f,
        StartSize = 0.34f,
        EndSize = 1.55f,
        Lifetime = 1.05f,
        Opacity = 0.75f,
        Color = new Color(150, 150, 150)
    };
}
