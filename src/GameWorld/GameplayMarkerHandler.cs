using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RTS;

public sealed class GameplayMarkerHandler
{
    public const string FileName = "gameplay-markers.json";
    private readonly List<GameplayMarker> _markers = [];
    public IReadOnlyList<GameplayMarker> Markers => _markers;

    public GameplayMarker Add(GameplayMarkerType type, Vector3 position, float rotationDegrees, float toolSize = 1.0f)
    {
        GameplayMarkerShape shape = DefaultShape(type);
        float size = Math.Max(1.0f, toolSize);
        GameplayMarker marker = new()
        {
            Type = type,
            Name = type.ToString(),
            Position = position,
            RotationDegrees = rotationDegrees,
            Shape = shape,
            Size = shape == GameplayMarkerShape.Point ? Vector2.One : new Vector2(size),
            PlayerSlot = type == GameplayMarkerType.PlayerStart
                ? _markers.Where(item => item.Type == GameplayMarkerType.PlayerStart).Select(item => item.PlayerSlot ?? 0).DefaultIfEmpty().Max() + 1
                : null
        };
        _markers.Add(marker);
        return marker;
    }

    public bool RemoveNearest(Vector3 position, float maximumDistance)
    {
        GameplayMarker? marker = FindNearest(position, maximumDistance);
        return marker is not null && _markers.Remove(marker);
    }

    public GameplayMarker? FindNearest(Vector3 position, float maximumDistance)
    {
        Vector2 target = new(position.X, position.Z);
        GameplayMarker? marker = _markers
            .OrderBy(item => Vector2.DistanceSquared(
                new Vector2(item.Position.X, item.Position.Z), target))
            .FirstOrDefault();
        return marker is not null && Vector2.DistanceSquared(
            new Vector2(marker.Position.X, marker.Position.Z), target) <= maximumDistance * maximumDistance
                ? marker
                : null;
    }

    public void Clear() => _markers.Clear();

    public GameplayMarkerState[] GetStates() => _markers.Select(ToState).ToArray();

    public void ApplyStates(IEnumerable<GameplayMarkerState>? states)
    {
        _markers.Clear();
        if (states is null) return;
        HashSet<Guid> ids = [];
        foreach (GameplayMarkerState state in states)
        {
            if (state.Id == Guid.Empty || !ids.Add(state.Id) || !float.IsFinite(state.X) || !float.IsFinite(state.Y) || !float.IsFinite(state.Z))
                continue;
            GameplayMarker marker = new()
            {
                Id = state.Id,
                Name = state.Name ?? state.Type.ToString(),
                Type = state.Type,
                Position = new Vector3(state.X, state.Y, state.Z),
                RotationDegrees = state.RotationDegrees,
                Shape = state.Shape,
                Size = new Vector2(Math.Max(1.0f, state.Width), Math.Max(1.0f, state.Height)),
                PlayerSlot = state.PlayerSlot,
                TeamId = state.TeamId
            };
            foreach (string tag in state.Tags ?? []) if (!string.IsNullOrWhiteSpace(tag)) marker.Tags.Add(tag);
            foreach ((string key, string value) in state.Properties ?? []) marker.Properties[key] = value;
            float[] points = state.PathPoints ?? [];
            for (int index = 0; index + 2 < points.Length; index += 3)
                marker.Points.Add(new Vector3(points[index], points[index + 1], points[index + 2]));
            _markers.Add(marker);
        }
    }

    public void Save(string mapDirectory)
    {
        Directory.CreateDirectory(mapDirectory);
        File.WriteAllText(Path.Combine(mapDirectory, FileName), JsonSerializer.Serialize(GetStates(), new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Load(string mapDirectory)
    {
        string path = Path.Combine(mapDirectory, FileName);
        ApplyStates(File.Exists(path) ? JsonSerializer.Deserialize<GameplayMarkerState[]>(File.ReadAllText(path)) : null);
    }

    public void DrawEditor(Camera camera, Terrain terrain, GameGrid grid)
    {
        foreach (GameplayMarker marker in _markers)
            DrawMarker(camera, terrain, grid, marker, ColorFor(marker.Type));
    }

    public void DrawPreview(Camera camera, Terrain terrain, GameGrid grid, GameplayMarkerType type, Vector3 position, float rotationDegrees, float toolSize)
    {
        GameplayMarker preview = new() { Type = type, Position = position, RotationDegrees = rotationDegrees, Shape = DefaultShape(type), Size = new Vector2(Math.Max(1, toolSize)) };
        DrawMarker(camera, terrain, grid, preview, ColorFor(type) * 0.6f);
    }

    public void DrawRemovalPreview(Camera camera, Terrain terrain, GameGrid grid,
        Vector3 position, float maximumDistance)
    {
        GameplayMarker? marker = FindNearest(position, maximumDistance);
        if (marker is not null)
            DrawMarker(camera, terrain, grid, marker, new Color(255, 30, 30, 210));
    }

    public void DrawLabels(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        foreach (GameplayMarker marker in _markers)
        {
            Vector3 screen = viewport.Project(marker.Position + Vector3.Up, camera.Projection, camera.View, Matrix.Identity);
            if (screen.Z is < 0 or > 1) continue;
            string text = marker.Type == GameplayMarkerType.PlayerStart ? $"Player Start {marker.PlayerSlot}" : marker.Name;
            RenderHelper.DrawTextCentered(spriteBatch, Globals._debugFont, text, new Vector2(screen.X, screen.Y), ColorFor(marker.Type));
        }
    }

    private static void DrawMarker(Camera camera, Terrain terrain, GameGrid grid, GameplayMarker marker, Color color)
    {
        foreach (Point cell in GetCells(marker, grid))
            for (int z = cell.Y * grid.CellSize; z < (cell.Y + 1) * grid.CellSize; z++)
                for (int x = cell.X * grid.CellSize; x < (cell.X + 1) * grid.CellSize; x++)
                    if (x >= 0 && z >= 0 && x < terrain.Width - 1 && z < terrain.Height - 1)
                        terrain.HighlightCell(camera, x, z, color);
    }

    private static IEnumerable<Point> GetCells(GameplayMarker marker, GameGrid grid)
    {
        Point center = grid.ToCell(marker.Position);
        if (marker.Shape == GameplayMarkerShape.Point) return [center];
        float halfWidth = marker.Size.X * 0.5f;
        float halfHeight = marker.Size.Y * 0.5f;
        int extent = (int)MathF.Ceiling(Math.Max(halfWidth, halfHeight));
        List<Point> cells = [];
        float angle = MathHelper.ToRadians(-marker.RotationDegrees);
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        for (int y = center.Y - extent; y <= center.Y + extent; y++)
            for (int x = center.X - extent; x <= center.X + extent; x++)
            {
                float dx = x + 0.5f - marker.Position.X / grid.CellSize;
                float dy = y + 0.5f - marker.Position.Z / grid.CellSize;
                float localX = dx * cos - dy * sin;
                float localY = dx * sin + dy * cos;
                bool inside = marker.Shape == GameplayMarkerShape.Circle
                    ? localX * localX + localY * localY <= halfWidth * halfWidth
                    : MathF.Abs(localX) <= halfWidth && MathF.Abs(localY) <= halfHeight;
                if (inside && grid.Contains(new Point(x, y))) cells.Add(new Point(x, y));
            }
        return cells;
    }

    private static GameplayMarkerShape DefaultShape(GameplayMarkerType type) => type switch
    {
        GameplayMarkerType.ExpansionSite or GameplayMarkerType.ObservationPoint or GameplayMarkerType.LandingZone or GameplayMarkerType.ResourceField or GameplayMarkerType.NoBuildZone or GameplayMarkerType.NoFlyZone => GameplayMarkerShape.Circle,
        GameplayMarkerType.DefensivePosition or GameplayMarkerType.TriggerPoint or GameplayMarkerType.ChokePoint or GameplayMarkerType.AIRegion => GameplayMarkerShape.Rectangle,
        _ => GameplayMarkerShape.Point
    };

    private static Color ColorFor(GameplayMarkerType type) => type switch
    {
        GameplayMarkerType.PlayerStart => Color.Lime,
        GameplayMarkerType.InitialCamera => Color.White,
        GameplayMarkerType.ResourceField => Color.Cyan,
        GameplayMarkerType.ExpansionSite => Color.Yellow,
        GameplayMarkerType.DefensivePosition => Color.OrangeRed,
        GameplayMarkerType.ObservationPoint => Color.LightBlue,
        GameplayMarkerType.LandingZone => Color.Magenta,
        _ => Color.Gold
    };

    private static GameplayMarkerState ToState(GameplayMarker marker) => new(
        marker.Id, marker.Name, marker.Type, marker.Position.X, marker.Position.Y, marker.Position.Z,
        marker.RotationDegrees, marker.Shape, marker.Size.X, marker.Size.Y, marker.PlayerSlot, marker.TeamId,
        marker.Tags.ToArray(), new Dictionary<string, string>(marker.Properties),
        marker.Points.SelectMany(point => new[] { point.X, point.Y, point.Z }).ToArray());
}
