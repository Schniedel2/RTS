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
    /// <summary>Seconds before a newly allocated particle becomes visible and starts moving.</summary>
    public float StartDelay { get; init; }
    /// <summary>Random variation around <see cref="StartDelay"/>; negative results are clamped to zero.</summary>
    public float StartDelayVariation { get; init; }
    public Color Color { get; init; } = new(184, 184, 184);
    public float Opacity { get; init; } = 0.62f;
    /// <summary>0 ignores wind; 1 drifts with the complete world wind velocity.</summary>
    public float WindInfluence { get; init; } = 0.85f;
}

/// <summary>Factories return new settings so callers can safely customize them.</summary>
public static class SmokeEmissionPresets
{
    public static SmokeEmissionSettings RifleFlash() => new()
    {
        ParticleCount = 3,
        Intensity = 0.75f,
        EmissionLength = 0.5f,
        ForwardSpeed = 0.1f,
        StartSize = 0.36f,
        EndSize = 0.1f,
        Lifetime = 0.48f,
        Opacity = 0.75f,
        WindInfluence = 0.0f,
        Color = new Color(255, 255, 255)
    };

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

    /// <summary>Small, dark, continuous exhaust puffs for vehicles and generators.</summary>
    public static SmokeEmissionSettings VehicleExhaust() => new()
    {
        ParticleCount = 1,
        Intensity = 0.85f,
        EmissionLength = 0.08f,
        ForwardSpeed = 0.22f,
        ForwardSpeedVariation = 0.10f,
        SidewaysSpread = 0.12f,
        UpwardSpeed = 0.35f,
        UpwardSpeedVariation = 0.18f,
        StartSize = 0.30f,
        StartSizeVariation = 0.04f,
        EndSize = 0.72f,
        EndSizeVariation = 0.14f,
        Lifetime = 1.45f,
        LifetimeVariation = 0.20f,
        Opacity = 0.68f,
        Color = new Color(48, 48, 48),
        WindInfluence = 0.95f
    };

    /// <summary>Dark, slowly rising smoke emitted by damaged units and buildings.</summary>
    public static SmokeEmissionSettings UnitDamage() => new()
    {
        ParticleCount = 1,
        Intensity = 1.0f,
        EmissionLength = 0.05f,
        ForwardSpeed = 0.08f,
        ForwardSpeedVariation = 0.06f,
        SidewaysSpread = 0.20f,
        UpwardSpeed = 0.62f,
        UpwardSpeedVariation = 0.20f,
        StartSize = 0.28f,
        StartSizeVariation = 0.07f,
        EndSize = 1.05f,
        EndSizeVariation = 0.22f,
        Lifetime = 2.1f,
        LifetimeVariation = 0.35f,
        Opacity = 0.78f,
        Color = new Color(24, 24, 24),
        WindInfluence = 0.95f
    };

    /// <summary>Dense, persistent smoke rising from a destroyed vehicle wreck.</summary>
    public static SmokeEmissionSettings VehicleWreck() => new()
    {
        ParticleCount = 2,
        Intensity = 1.35f,
        EmissionLength = 0.10f,
        ForwardSpeed = 0.10f,
        ForwardSpeedVariation = 0.08f,
        SidewaysSpread = 0.30f,
        UpwardSpeed = 0.85f,
        UpwardSpeedVariation = 0.28f,
        StartSize = 0.38f,
        StartSizeVariation = 0.10f,
        EndSize = 1.65f,
        EndSizeVariation = 0.38f,
        Lifetime = 2.8f,
        LifetimeVariation = 0.45f,
        Opacity = 0.86f,
        Color = new Color(20, 18, 18),
        WindInfluence = 0.92f
    };

    public static SmokeEmissionSettings RifleSmoke() => new()
    {
        ParticleCount = 1,
        Intensity = 0.85f,
        EmissionLength = 0.08f,
        ForwardSpeed = 0.22f,
        ForwardSpeedVariation = 0.10f,
        SidewaysSpread = 0.12f,
        UpwardSpeed = 0.35f,
        UpwardSpeedVariation = 0.18f,
        StartSize = 0.30f,
        StartSizeVariation = 0.04f,
        EndSize = 0.72f,
        EndSizeVariation = 0.14f,
        Lifetime = 1.45f,
        LifetimeVariation = 0.20f,
        Opacity = 0.68f,
        Color = new Color(220, 220, 220),
        WindInfluence = 0.95f
    };

    public static SmokeEmissionSettings RpgMuzzle() => new()
    {
        ParticleCount = 5,
        Intensity = 0.9f,
        EmissionLength = 0.35f,
        ForwardSpeed = 1.4f,
        ForwardSpeedVariation = 0.45f,
        SidewaysSpread = 0.5f,
        UpwardSpeed = 0.2f,
        StartSize = 0.18f,
        EndSize = 0.75f,
        Lifetime = 0.75f,
        Opacity = 0.72f,
        Color = new Color(205, 205, 195),
        WindInfluence = 0.65f
    };

    public static SmokeEmissionSettings RpgBackblast() => new()
    {
        ParticleCount = 10,
        Intensity = 1.15f,
        EmissionLength = 1.2f,
        ForwardSpeed = 2.8f,
        ForwardSpeedVariation = 0.8f,
        SidewaysSpread = 1.1f,
        UpwardSpeed = 0.25f,
        StartSize = 0.22f,
        EndSize = 1.05f,
        Lifetime = 1.0f,
        Opacity = 0.78f,
        Color = new Color(185, 180, 165),
        WindInfluence = 0.75f
    };

    public static SmokeEmissionSettings RocketTrail(float exhaustStrength = 1.0f) => new()
    {
        ParticleCount = exhaustStrength >= 0.55f ? 2 : 1,
        Intensity = 0.25f + exhaustStrength * 1.15f,
        EmissionLength = 0.07f,
        ForwardSpeed = 0.32f,
        ForwardSpeedVariation = 0.14f,
        SidewaysSpread = 0.24f,
        UpwardSpeed = 0.10f,
        StartSize = 0.20f,
        EndSize = 0.72f,
        Lifetime = 1.25f,
        LifetimeVariation = 0.18f,
        Opacity = 0.72f,
        Color = new Color(250, 250, 250),
        WindInfluence = 0.82f
    };

    public static SmokeEmissionSettings ReactorExhaust() => new()
    {
        //  big white
        Color = new Color(255, 255, 255),
        ParticleCount = 3,
        Intensity = 0.8f,
        EmissionLength = 0.08f,
        UpwardSpeed = 1.2f,
        ForwardSpeed = 0.0f,
        StartSize = 4.5f,
        StartSizeVariation = 0.5f,
        EndSize = 8.5f,
        EndSizeVariation = 0.5f,
        Lifetime = 4.0f,
        Opacity = 0.65f
    };

    public static SmokeEmissionSettings DestroyBuilding()
    {
        return SmokeEmissionPresets.VehicleWreck() with
        {
            ParticleCount = 3,
            Intensity = 2.2f,
            StartSize = 0.75f,
            EndSize = 3.4f,
            Lifetime = 4.5f,
            StartDelay = 0f,
            StartDelayVariation = 3.0f,
            Opacity = 0.92f,
            Color = new(150, 150, 150)
        };
    }
}
