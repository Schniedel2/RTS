using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace RTS;

/// <summary>
/// Describes sprite sheets which have already been inserted into a
/// <see cref="TextureHandler"/> atlas. It does not own or load textures.
/// </summary>
public sealed class TilemapHandler
{
    public sealed class Tilemap
    {
        private readonly TextureHandler.TextureRegion _textureRegion;

        internal Tilemap(
            string name,
            TextureHandler.TextureRegion textureRegion,
            Point tileSize,
            Point tilePadding,
            Point outerPadding,
            int columns,
            int rows)
        {
            Name = name;
            _textureRegion = textureRegion;
            TileSize = tileSize;
            TilePadding = tilePadding;
            OuterPadding = outerPadding;
            Columns = columns;
            Rows = rows;
        }

        public string Name { get; }
        public TextureHandler.TextureRegion TextureRegion => _textureRegion;
        public int AtlasIndex => _textureRegion.AtlasIndex;
        public Point TileSize { get; }
        /// <summary>Transparent spacing between two neighboring source tiles, in pixels.</summary>
        public Point TilePadding { get; }
        /// <summary>Padding between the texture border and the first/last source tile, in pixels.</summary>
        public Point OuterPadding { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int TileCount => Columns * Rows;

        /// <summary>Returns the content rectangle in the original tile-sheet texture.</summary>
        public Rectangle GetSourceRectangle(int tileIndex)
        {
            ValidateTileIndex(tileIndex);
            int column = tileIndex % Columns;
            int row = tileIndex / Columns;
            return new Rectangle(
                OuterPadding.X + column * (TileSize.X + TilePadding.X),
                OuterPadding.Y + row * (TileSize.Y + TilePadding.Y),
                TileSize.X,
                TileSize.Y);
        }

        /// <summary>Returns the content rectangle in the TextureHandler atlas.</summary>
        public Rectangle GetAtlasRectangle(int tileIndex)
        {
            Rectangle source = GetSourceRectangle(tileIndex);
            return new Rectangle(
                _textureRegion.X + source.X,
                _textureRegion.Y + source.Y,
                source.Width,
                source.Height);
        }

        /// <summary>Returns UV offset and scale for a tile in its TextureHandler atlas.</summary>
        public (Vector2 Offset, Vector2 Scale) GetAtlasUV(int tileIndex)
        {
            Rectangle atlas = GetAtlasRectangle(tileIndex);
            return (
                new Vector2(
                    (float)atlas.X / _textureRegion.AtlasWidth,
                    (float)atlas.Y / _textureRegion.AtlasHeight),
                new Vector2(
                    (float)atlas.Width / _textureRegion.AtlasWidth,
                    (float)atlas.Height / _textureRegion.AtlasHeight));
        }

        private void ValidateTileIndex(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= TileCount)
                throw new ArgumentOutOfRangeException(nameof(tileIndex), tileIndex,
                    $"Tilemap '{Name}' contains tile indices from 0 to {TileCount - 1}.");
        }
    }

    private readonly Dictionary<string, Tilemap> _tilemaps =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, Tilemap> Tilemaps => _tilemaps;

    /// <summary>
    /// Registers a uniformly tiled source image that already exists in a
    /// TextureHandler atlas. <paramref name="tilePadding"/> is the gap between
    /// tiles; <paramref name="outerPadding"/> is the sheet border.
    /// </summary>
    public Tilemap Register(
        string name,
        TextureHandler.TextureRegion textureRegion,
        Point tileSize,
        Point? tilePadding = null,
        Point? outerPadding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(textureRegion);
        if (tileSize.X <= 0 || tileSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize), "Tile dimensions must be positive.");

        Point spacing = tilePadding ?? Point.Zero;
        Point border = outerPadding ?? Point.Zero;
        if (spacing.X < 0 || spacing.Y < 0 || border.X < 0 || border.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(tilePadding), "Tile padding cannot be negative.");

        int columns = CalculateTileCount(textureRegion.Width, tileSize.X, spacing.X, border.X, "width");
        int rows = CalculateTileCount(textureRegion.Height, tileSize.Y, spacing.Y, border.Y, "height");
        Tilemap tilemap = new(name, textureRegion, tileSize, spacing, border, columns, rows);
        _tilemaps[name] = tilemap;
        return tilemap;
    }

    public bool TryGet(string name, out Tilemap tilemap) =>
        _tilemaps.TryGetValue(name, out tilemap!);

    public Tilemap Get(string name) =>
        TryGet(name, out Tilemap tilemap)
            ? tilemap
            : throw new KeyNotFoundException($"Tilemap '{name}' is not registered.");

    private static int CalculateTileCount(int textureSize, int tileSize, int spacing, int border, string axis)
    {
        int usableSize = textureSize - border * 2;
        if (usableSize < tileSize)
            throw new ArgumentException($"Tile size exceeds the usable texture {axis}.");

        int stride = tileSize + spacing;
        int remainder = (usableSize - tileSize) % stride;
        if (remainder != 0)
            throw new ArgumentException(
                $"Texture {axis} does not fit an integral tile grid with the supplied size and padding.");
        return 1 + (usableSize - tileSize) / stride;
    }

    public void LoadTilemaps()
    {
        LoadTilemaps(Globals.EffetcsDirectory);
    }

    public void LoadTilemaps(string directory)
    {
        // Example tilemap registration
        //TextureHandler.TextureRegion smokeRegion = Globals.TextureHandler.AddTexture(Path.Combine(directory, "smoke-256x256.png"));
        //TextureHandler.TextureRegion explosionRegion = Globals.TextureHandler.AddTexture(Path.Combine(directory, "Explosion.png"));
        //TextureHandler.TextureRegion sparksRegion = Globals.TextureHandler.AddTexture(Path.Combine(directory, "sparks-256x256.png"));

        Globals.TilemapHandler.Register("Smoke", Globals.TextureHandler.AddTexture(Path.Combine(directory, "smoke-256x256.png")), new Point(256, 256));
        Globals.TilemapHandler.Register("Sparks", Globals.TextureHandler.AddTexture(Path.Combine(directory, "sparks-256x256.png")), new Point(256, 256));
        Globals.TilemapHandler.Register("Explosion", Globals.TextureHandler.AddTexture(Path.Combine(directory, "explosions-256x256.png")), new Point(256, 256));
        Globals.TilemapHandler.Register("Scorch", Globals.TextureHandler.AddTexture(Path.Combine(directory, "scorch-256x256.png")), new Point(256, 256));
        Globals.TilemapHandler.Register("Rubble", Globals.TextureHandler.AddTexture(Path.Combine(directory, "rubble-256x256.png")), new Point(256, 256));
    }
}
