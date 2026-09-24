using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>Visual and local-wind parameters for one impact explosion.</summary>
public sealed record ExplosionEmissionSettings
{
    // Kept configurable because the atlas name is supplied by content creators.
    public string ExplosionTilemapName { get; init; } = "Explosion";
    public string SparksTilemapName { get; init; } = "Sparks";
    public int FireParticleCount { get; init; } = 28;
    public int DebrisCount { get; init; } = 8;
    public int SmokeBurstCount { get; init; }
    public bool CreateScorchMark { get; init; } = true;
    public float Intensity { get; init; } = 1.0f;
    public float DebrisSpeed { get; init; } = 5.5f;
    public float DebrisSpeedVariation { get; init; } = 2.5f;
    public float DebrisLifetime { get; init; } = 0.65f;
    public float DebrisSmokeInterval { get; init; } = 0.075f;
    public float ShockwaveRadius { get; init; } = 5.0f;
    public float ShockwaveStrength { get; init; } = 8.0f;
    public float ShockwaveLifetime { get; init; } = 0.45f;
    public SmokeEmissionSettings DebrisSmokeSettings { get; init; } = SmokeEmissionPresets.LightCannon() with
    {
        ParticleCount = 1,
        Intensity = 0.8f,
        ForwardSpeed = 0.15f,
        UpwardSpeed = 0.0f,
        StartSize = 0.12f,
        EndSize = 0.42f,
        Lifetime = 0.55f,
        Opacity = 0.50f,
        Color = new Color(45, 45, 45),
        WindInfluence = 1.0f
    };
    public SmokeEmissionSettings SmokeBurstSettings { get; init; } = SmokeEmissionPresets.VehicleWreck();
}

public static class ExplosionEmissionPresets
{
    public static ExplosionEmissionSettings TankShell() => new()
    {
        FireParticleCount = 10,
        DebrisCount = 28,
        Intensity = 1.45f,
        DebrisSpeed = 5.5f,
        ShockwaveRadius = 4.0f,
        ShockwaveStrength = 5.0f,
        ShockwaveLifetime = 0.65f,
        DebrisSmokeInterval = 0.1f
    };

    public static ExplosionEmissionSettings RifleFlash() => new()
    {
        FireParticleCount = 3,
        DebrisCount = 0,
        Intensity = 0.45f,
        DebrisSpeed = 0f,
        ShockwaveRadius = 0.0f,
        ShockwaveStrength = 0.0f,
        ShockwaveLifetime = 0.0f,
        DebrisSmokeInterval = 0.1f
    };

    public static ExplosionEmissionSettings HeavyShell() => new()
    {
        FireParticleCount = 42,
        DebrisCount = 14,
        Intensity = 1.45f,
        DebrisSpeed = 7.5f,
        ShockwaveRadius = 8.0f,
        ShockwaveStrength = 13.0f,
        ShockwaveLifetime = 0.65f
    };

    public static ExplosionEmissionSettings VehicleDestruction() => new()
    {
        FireParticleCount = 64,
        DebrisCount = 22,
        SmokeBurstCount = 10,
        Intensity = 1.75f,
        DebrisSpeed = 8.0f,
        DebrisSpeedVariation = 3.5f,
        DebrisLifetime = 1.0f,
        DebrisSmokeInterval = 0.07f,
        ShockwaveRadius = 9.0f,
        ShockwaveStrength = 15.0f,
        ShockwaveLifetime = 0.8f,
        SmokeBurstSettings = SmokeEmissionPresets.VehicleWreck() with
        {
            ParticleCount = 2,
            Intensity = 1.7f,
            StartSize = 0.52f,
            EndSize = 2.2f,
            Lifetime = 3.2f
        }
    };
}
