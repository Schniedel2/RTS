using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;

namespace RTS;

public sealed class Minimap : IDisposable
{
    private const int Size = 220;
    private const int TextureSize = 128;
    private readonly GameWorld _world;
    private readonly VisibilitySystem _visibility;
    private readonly Texture2D _texture;
    private readonly Color[] _pixels;
    private MouseState _previousMouse;
    private Rectangle _bounds;

    public Minimap(GraphicsDevice graphicsDevice, GameWorld world, VisibilitySystem visibility)
    {
        _world = world;
        _visibility = visibility;
        _texture = new Texture2D(
            graphicsDevice,
            Math.Min(TextureSize, world.GameGrid.Width),
            Math.Min(TextureSize, world.GameGrid.Height),
            false,
            SurfaceFormat.Color);
        _pixels = new Color[_texture.Width * _texture.Height];
    }

    public bool Update(Camera camera, Viewport viewport)
    {
        _bounds = new Rectangle(viewport.Width - Size - 12, viewport.Height - Size - 12, Size, Size);
        MouseState mouse = Mouse.GetState();
        bool pressed = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        bool consumed = _bounds.Contains(mouse.Position) && mouse.LeftButton == ButtonState.Pressed;
        if (consumed && (pressed || mouse.LeftButton == ButtonState.Pressed))
        {
            float normalizedX = (mouse.X - _bounds.X) / (float)_bounds.Width;
            float normalizedY = (mouse.Y - _bounds.Y) / (float)_bounds.Height;
            camera.CenterOn(new Vector2(normalizedX * _world.Terrain.Width, normalizedY * _world.Terrain.Height));
        }
        _previousMouse = mouse;
        return consumed;
    }

    public void Refresh(Guid viewerArmyId)
    {
        int width = _texture.Width;
        var alliedArmyIds = _visibility.GetAlliedArmyIds(viewerArmyId);
        for (int y = 0; y < _texture.Height; y++)
            for (int x = 0; x < width; x++)
            {
                Point cell = new(
                    Math.Min(_world.GameGrid.Width - 1, (int)((x + 0.5f) * _world.GameGrid.Width / width)),
                    Math.Min(_world.GameGrid.Height - 1, (int)((y + 0.5f) * _world.GameGrid.Height / _texture.Height)));
                VisibilityState state = _visibility.GetDisplayedTerrainVisibility(viewerArmyId, cell, true, alliedArmyIds);
                Color terrain = TerrainColor(_world.Terrain.GetTile(
                    Math.Min(cell.X * _world.GameGrid.CellSize, _world.Terrain.Width - 1),
                    Math.Min(cell.Y * _world.GameGrid.CellSize, _world.Terrain.Height - 1)));
                _pixels[y * width + x] = state switch
                {
                    VisibilityState.Visible => terrain,
                    VisibilityState.Explored => terrain * 0.38f,
                    _ => Color.Black
                };
            }

        foreach (Unit unit in _world.Units.Units)
        {
            if (unit.IsEmbarked || !_visibility.IsUnitVisible(viewerArmyId, unit, true)) continue;
            Point cell = _world.GameGrid.ToCell(unit.Position);
            if (!_world.GameGrid.Contains(cell)) continue;
            int markerX = Math.Min(_texture.Width - 1, cell.X * _texture.Width / _world.GameGrid.Width);
            int markerY = Math.Min(_texture.Height - 1, cell.Y * _texture.Height / _world.GameGrid.Height);
            Color color = unit.ArmyId == viewerArmyId ? Color.Lime : IsAlly(viewerArmyId, unit.ArmyId) ? Color.Cyan : Color.Red;
            int radius = unit is Building ? 1 : 0;
            for (int py = Math.Max(0, markerY - radius); py <= Math.Min(_texture.Height - 1, markerY + radius); py++)
                for (int px = Math.Max(0, markerX - radius); px <= Math.Min(_texture.Width - 1, markerX + radius); px++)
                    _pixels[py * width + px] = color;
        }
        _texture.SetData(_pixels);
    }

    public void Draw(SpriteBatch spriteBatch, Camera camera)
    {
        spriteBatch.Draw(Globals._whiteTexture, new Rectangle(_bounds.X - 2, _bounds.Y - 2, _bounds.Width + 4, _bounds.Height + 4), Color.Black);
        spriteBatch.Draw(_texture, _bounds, Color.White);
        int x = _bounds.X + (int)(camera.Position.X / _world.Terrain.Width * _bounds.Width);
        int y = _bounds.Y + (int)(camera.Position.Z / _world.Terrain.Height * _bounds.Height);
        spriteBatch.Draw(Globals._whiteTexture, new Rectangle(x - 5, y - 1, 11, 3), Color.White);
        spriteBatch.Draw(Globals._whiteTexture, new Rectangle(x - 1, y - 5, 3, 11), Color.White);
    }

    private static bool IsAlly(Guid viewerArmyId, Guid? otherArmyId)
    {
        Army? viewer = Globals.Game.Armies.Find(viewerArmyId);
        Army? other = otherArmyId is Guid id ? Globals.Game.Armies.Find(id) : null;
        return viewer?.TeamId is Guid team && other?.TeamId == team;
    }

    private static Color TerrainColor(TerrainTile tile) => tile switch
    {
        TerrainTile.Grass or TerrainTile.GrassWIthDirt or TerrainTile.GrassWithSoil => new Color(45, 105, 45),
        TerrainTile.Sand or TerrainTile.DrySoil => new Color(174, 150, 88),
        TerrainTile.Concrete => new Color(125, 125, 125),
        TerrainTile.Rock or TerrainTile.Stones or TerrainTile.Stones2 => new Color(85, 85, 80),
        _ => new Color(105, 75, 48)
    };

    public void Dispose() => _texture.Dispose();
}
