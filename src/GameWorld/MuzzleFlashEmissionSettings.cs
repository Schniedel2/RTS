using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>Visual settings for one short, directed small-arms muzzle flash.</summary>
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
    public SmokeEmissionSettings SmokeSettings { get; init; } = SmokeEmissionPresets.RifleSmoke();
}

public static class MuzzleFlashEmissionPresets
{
    public static MuzzleFlashEmissionSettings Rifle() => new();
}
