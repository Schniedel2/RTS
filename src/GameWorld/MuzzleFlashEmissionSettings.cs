using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>Visual settings for one short, directed muzzle flash.</summary>
public sealed record MuzzleFlashEmissionSettings
{
    public string TilemapName { get; init; } = "Sparks";
    public int SecondaryFlashCount { get; init; } = 2;
    public float MainStartSize { get; init; } = 0.48f;
    public float MainEndSize { get; init; } = 0.035f;
    public float SecondaryStartSize { get; init; } = 0.24f;
    public float SecondaryEndSize { get; init; } = 0.02f;
    public float Lifetime { get; init; } = 0.075f;
    public float SecondaryDistance { get; init; } = 0.20f;
    public float SecondarySpeed { get; init; } = 3.2f;
    public Color Color { get; init; } = new(255, 238, 170);
    public SmokeEmissionSettings SmokeSettings { get; init; } = SmokeEmissionPresets.TurretSmoke();
}

public static class MuzzleFlashEmissionPresets
{
    public static MuzzleFlashEmissionSettings Rifle() => new();

    public static MuzzleFlashEmissionSettings TankCannon() => new()
    {
        TilemapName = "Explosion",
        SecondaryFlashCount = 5,
        MainStartSize = 1.6f,
        MainEndSize = 2.08f,
        SecondaryStartSize = 1.72f,
        SecondaryEndSize = 0.035f,
        Lifetime = 0.31f,
        SecondaryDistance = 0.85f,
        SecondarySpeed = 5.5f,
        Color = new Color(255, 220, 115),
        SmokeSettings = SmokeEmissionPresets.TankCannon()
    };

    public static MuzzleFlashEmissionSettings Turret() => new()
    {
        TilemapName = "MuzzleFlash",
        MainStartSize = 1.08f,
        MainEndSize = 0.35f,
        SecondaryFlashCount = 10,
        SecondaryStartSize = 0.8f,        
        SecondaryEndSize = 0.02f,
        SecondaryDistance = 0.85f,
        SecondarySpeed = 5.5f,
        SmokeSettings = SmokeEmissionPresets.TurretSmoke()
        /*
        TilemapName = "MuzzleFlash",
        MainStartSize = 0.72f,
        MainEndSize = 0.18f,
        SecondaryFlashCount = 1,
        SecondaryStartSize = 0.30f,
        SecondaryEndSize = 0.04f,
        Lifetime = 0.065f,
        SecondaryDistance = 0.18f,
        SecondarySpeed = 2.2f,
        Color = Color.White,
        SmokeSettings = SmokeEmissionPresets.TurretSmoke()
        */
    };
}
