using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace RTS;

public sealed class FogOfWarTexture : IDisposable
{
    private const int MaximumTextureSize = 128;
    private readonly VisibilitySystem _visibility;
    private readonly GameGrid _grid;
    private readonly Texture2D _texture;
    private readonly Color[] _pixels;
    public Texture2D Texture => _texture;

    public FogOfWarTexture(GraphicsDevice graphicsDevice, GameGrid grid, VisibilitySystem visibility)
    {
        _visibility = visibility;
        _grid = grid;
        int width = Math.Min(MaximumTextureSize, grid.Width);
        int height = Math.Min(MaximumTextureSize, grid.Height);
        _texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
        _pixels = new Color[width * height];
    }

    public void Update(Guid viewerArmyId)
    {
        int width = _texture.Width;
        var alliedArmyIds = _visibility.GetAlliedArmyIds(viewerArmyId);
        for (int y = 0; y < _texture.Height; y++)
            for (int x = 0; x < width; x++)
            {
                Point cell = new(
                    Math.Min(_grid.Width - 1, (int)((x + 0.5f) * _grid.Width / width)),
                    Math.Min(_grid.Height - 1, (int)((y + 0.5f) * _grid.Height / _texture.Height)));
                byte value = _visibility.GetDisplayedTerrainVisibility(viewerArmyId, cell, false, alliedArmyIds) switch
                {
                    VisibilityState.Visible => (byte)255,
                    VisibilityState.Explored => (byte)105,
                    _ => (byte)8
                };
                _pixels[y * width + x] = new Color(value, value, value, (byte)255);
            }
        _texture.SetData(_pixels);
    }

    public void Dispose() => _texture.Dispose();
}
