using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

/// <summary>Stable, network-safe IDs for the selectable player camouflage skins.</summary>
public enum PlayerSkin
{
    Green, Blue, Gray, Yellow, Pink, Cyan, Violet, Red, Brown, Fancy,
    YellowBrown, LightGreen, Sand, Snow, Safari, Orange, LightPink,
    LightOrange, DarkGray
}

/// <summary>Maps player-facing skin names to atlas tiles and optional UI colors.</summary>
public sealed class SkinHandler
{
    public sealed record SkinDefinition(PlayerSkin Skin, string Name, int TileIndex, Color DisplayColor);

    private static readonly SkinDefinition[] Definitions =
    [
        new(PlayerSkin.Green, "Green", 0, Color.LimeGreen), new(PlayerSkin.Blue, "Blue", 1, Color.RoyalBlue),
        new(PlayerSkin.Gray, "Gray", 2, Color.Gray), new(PlayerSkin.Yellow, "Yellow", 3, Color.Yellow),
        new(PlayerSkin.Pink, "Pink", 4, Color.HotPink), new(PlayerSkin.Cyan, "Cyan", 5, Color.Cyan),
        new(PlayerSkin.Violet, "Violet", 6, Color.MediumPurple), new(PlayerSkin.Red, "Red", 7, Color.Red),
        new(PlayerSkin.Brown, "Brown", 8, Color.SaddleBrown), new(PlayerSkin.Fancy, "Fancy", 9, Color.MediumPurple),
        new(PlayerSkin.YellowBrown, "Yellow-Brown", 10, Color.Goldenrod), new(PlayerSkin.LightGreen, "Light-Green", 11, Color.LightGreen),
        new(PlayerSkin.Sand, "Sand", 12, Color.SandyBrown), new(PlayerSkin.Snow, "Snow", 13, Color.White),
        new(PlayerSkin.Safari, "Safari", 14, Color.OliveDrab), new(PlayerSkin.Orange, "Orange", 15, Color.Orange),
        new(PlayerSkin.LightPink, "Light-Pink", 16, Color.LightPink), new(PlayerSkin.LightOrange, "Light-Orange", 17, Color.LightSalmon),
        new(PlayerSkin.DarkGray, "Dark-Gray", 18, Color.DarkGray)
    ];

    private readonly Dictionary<string, SkinDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PlayerSkin, SkinDefinition> _bySkin = [];

    public SkinHandler()
    {
        foreach (SkinDefinition definition in Definitions)
        {
            _bySkin.Add(definition.Skin, definition);
            _byName.Add(definition.Name, definition);
        }
    }

    public IReadOnlyCollection<SkinDefinition> Skins => Definitions;
    public void LoadSkinTextures() => LoadSkinTextures(Globals.ModelsDirectory);

    public void LoadSkinTextures(string directory)
    {
        Globals.TextureHandler.NextTexture();
        TextureHandler.TextureRegion region = Globals.TextureHandler.AddTexture(Path.Combine(directory, "PlayerSkins-256x256.png"));
        Globals.TilemapHandler.Register("PlayerSkins", region, new Point(256, 256));
    }

    public bool TryParse(string name, out PlayerSkin skin)
    {
        if (_byName.TryGetValue(name, out SkinDefinition? definition))
        {
            skin = definition.Skin;
            return true;
        }
        skin = PlayerSkin.Green;
        return false;
    }

    public SkinDefinition Get(PlayerSkin skin) =>
        _bySkin.TryGetValue(skin, out SkinDefinition? definition) ? definition : _bySkin[PlayerSkin.Green];

    public Color GetDisplayColor(PlayerSkin skin) => Get(skin).DisplayColor;

    public void DisableForEffect(Effect effect) =>
        effect.Parameters["PlayerSkinStrength"]?.SetValue(0.0f);

    /// <summary>Sets the selected atlas tile for the next unit draw call.</summary>
    public void ApplyToEffect(Effect effect, PlayerSkin skin)
    {
        if (!Globals.TilemapHandler.TryGet("PlayerSkins", out TilemapHandler.Tilemap tilemap) || tilemap.TileCount == 0)
        {
            effect.Parameters["PlayerSkinStrength"]?.SetValue(0.0f);
            return;
        }

        int tileIndex = Get(skin).TileIndex % tilemap.TileCount;
        (Vector2 offset, Vector2 scale) = tilemap.GetAtlasUV(tileIndex);
        effect.Parameters["PlayerSkinTexture"]?.SetValue(Globals.TextureHandler.GetAtlas(tilemap.AtlasIndex));
        effect.Parameters["PlayerSkinUVOffset"]?.SetValue(offset);
        effect.Parameters["PlayerSkinUVScale"]?.SetValue(scale);
        effect.Parameters["PlayerSkinUVRepeat"]?.SetValue(32.0f);
        effect.Parameters["PlayerSkinStrength"]?.SetValue(1.0f);
    }
}
