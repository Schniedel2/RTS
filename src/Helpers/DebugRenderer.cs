using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class DebugRenderer
{
    private readonly BasicEffect _effect;

    public DebugRenderer()
    {
        _effect = new BasicEffect(Globals.GraphicsDevice)
        {
            VertexColorEnabled = true,
            LightingEnabled = false
        };
    }

    public void DrawLines(
        VertexPositionColor[] vertices,
        Matrix view,
        Matrix projection)
    {
        _effect.World = Matrix.Identity;
        _effect.View = view;
        _effect.Projection = projection;

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            Globals.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.LineList,
                vertices,
                0,
                vertices.Length / 2);
        }
    }

    /// <summary>Draws the right (red), up (green) and forward (blue) axis of every unit transform.</summary>
    public void DrawUnitTransforms(
        GameWorld world,
        Matrix view,
        Matrix projection)
    {
        var vertices = new List<VertexPositionColor>();

        foreach (Unit unit in world.Units.Units)
        {
            Matrix transform = unit.Transform;
            Vector3 origin = transform.Translation + new Vector3(0.0f, 0.1f, 0.0f);

            AddAxis(vertices, origin, transform.Right, (unit.Width + 1.0f), Color.Red);
            AddAxis(vertices, origin, transform.Up, (unit.Height + 1.0f), Color.Lime);
            AddAxis(vertices, origin, transform.Forward, (unit.Length), Color.DeepSkyBlue);
        }

        if (vertices.Count > 0)
            DrawLines(vertices.ToArray(), view, projection);
    }

    private static void AddAxis(
        List<VertexPositionColor> vertices,
        Vector3 origin,
        Vector3 direction,
        float length,
        Color color)
    {
        if (direction.LengthSquared() <= 0.0001f)
            return;

        vertices.Add(new VertexPositionColor(origin, color));
        vertices.Add(new VertexPositionColor(origin + Vector3.Normalize(direction) * length, color));
    }

    public void DrawSelectedUnitPaths(
        GameWorld world,
        Matrix view,
        Matrix projection)
    {
        var vertices = new List<VertexPositionColor>();
        const float heightOffset = 0.16f;

        foreach (Unit unit in world.Units.Units)
        {
            MobileUnit mobileUnit = unit as MobileUnit;
            if (mobileUnit is null || !mobileUnit.IsSelected || mobileUnit.PlannedPath.Count == 0)
                continue;

            Vector3 previous = mobileUnit.Position + Vector3.Up * heightOffset;
            foreach (Point waypoint in mobileUnit.PlannedPath)
            {
                Vector3 point = world.GameGrid.ToWorldPosition(waypoint, 0.0f);
                point.Y = world.Terrain.GetHeight(waypoint.X, waypoint.Y) + heightOffset;

                vertices.Add(new VertexPositionColor(previous, Color.Lime));
                vertices.Add(new VertexPositionColor(point, Color.Lime));

                // A small cross makes every individual cell of the path visible.
                const float markerRadius = 0.18f;
                vertices.Add(new VertexPositionColor(
                    point + new Vector3(-markerRadius, 0.0f, 0.0f), Color.Yellow));
                vertices.Add(new VertexPositionColor(
                    point + new Vector3(markerRadius, 0.0f, 0.0f), Color.Yellow));
                vertices.Add(new VertexPositionColor(
                    point + new Vector3(0.0f, 0.0f, -markerRadius), Color.Yellow));
                vertices.Add(new VertexPositionColor(
                    point + new Vector3(0.0f, 0.0f, markerRadius), Color.Yellow));

                previous = point;
            }
        }

        if (vertices.Count > 0)
            DrawLines(vertices.ToArray(), view, projection);
    }

    /*
    public void DrawTileMapGrid(
        GameMap gameMap,
        Matrix view,
        Matrix projection)
    {
        var vertices = new List<VertexPositionColor>();

        var tileMap = gameMap.TileMap;
        var terrain = gameMap.Terrain;

        float cellSize =
            gameMap.TileSize /
            gameMap.TerrainCellsPerTile;

        const float heightOffset = 0.05f;

        // ============================================================
        // Vertikale Tile-Grenzen
        // ============================================================

        for (int tileX = 0; tileX <= tileMap.Width; tileX++)
        {
            float worldX =
                tileX * gameMap.TileSize;

            int terrainX =
                tileX * gameMap.TerrainCellsPerTile;

            // Für jede Terrain-Zelle entlang der Grenze
            for (int z = 0; z < terrain.Height - 1; z++)
            {
                float worldZ1 =
                    z * terrain.CellSize;

                float worldZ2 =
                    (z + 1) * terrain.CellSize;

                float y1 =
                    terrain.GetHeight(terrainX, z) +
                    heightOffset;

                float y2 =
                    terrain.GetHeight(terrainX, z + 1) +
                    heightOffset;

                vertices.Add(new VertexPositionColor(
                    new Vector3(
                        worldX,
                        y1,
                        worldZ1),
                    Color.Lime));

                vertices.Add(new VertexPositionColor(
                    new Vector3(
                        worldX,
                        y2,
                        worldZ2),
                    Color.Lime));
            }
        }


        // ============================================================
        // Horizontale Tile-Grenzen
        // ============================================================

        for (int tileZ = 0; tileZ <= tileMap.Height; tileZ++)
        {
            float worldZ =
                tileZ * gameMap.TileSize;

            int terrainZ =
                tileZ * gameMap.TerrainCellsPerTile;

            // Für jede Terrain-Zelle entlang der Grenze
            for (int x = 0; x < terrain.Width - 1; x++)
            {
                float worldX1 =
                    x * terrain.CellSize;

                float worldX2 =
                    (x + 1) * terrain.CellSize;

                float y1 =
                    terrain.GetHeight(x, terrainZ) +
                    heightOffset;

                float y2 =
                    terrain.GetHeight(x + 1, terrainZ) +
                    heightOffset;

                vertices.Add(new VertexPositionColor(
                    new Vector3(
                        worldX1,
                        y1,
                        worldZ),
                    Color.Lime));

                vertices.Add(new VertexPositionColor(
                    new Vector3(
                        worldX2,
                        y2,
                        worldZ),
                    Color.Lime));
            }
        }

        DrawLines(
            vertices.ToArray(),
            view,
            projection);
    }
    */

    public void DrawTerrainGrid(
        GameWorld gameMap,
        Matrix view,
        Matrix projection)
    {
        var vertices = new List<VertexPositionColor>();

        const float heightOffset = 0.05f;
        var terrain = gameMap.Terrain;

        // ============================================================
        // Horizontale Kanten (X-Richtung)
        // ============================================================

        for (int z = 0; z < terrain.Height; z++)
        {
            for (int x = 0; x < terrain.Width - 1; x++)
            {
                float x1 = x;
                float x2 = (x + 1);

                float zWorld = z;

                float y1 =
                    terrain.GetHeight(x, z) +
                    heightOffset;

                float y2 =
                    terrain.GetHeight(x + 1, z) +
                    heightOffset;

                vertices.Add(new VertexPositionColor(
                    new Vector3(x1, y1, zWorld),
                    Color.Red));

                vertices.Add(new VertexPositionColor(
                    new Vector3(x2, y2, zWorld),
                    Color.Red));
            }
        }

        // ============================================================
        // Vertikale Kanten (Z-Richtung)
        // ============================================================

        for (int x = 0; x < terrain.Width; x++)
        {
            for (int z = 0; z < terrain.Height - 1; z++)
            {
                float xWorld = x;
                float z1 = z;
                float z2 = (z + 1);

                float y1 =
                    terrain.GetHeight(x, z) +
                    heightOffset;

                float y2 =
                    terrain.GetHeight(x, z + 1) +
                    heightOffset;

                vertices.Add(new VertexPositionColor(
                    new Vector3(
                        xWorld,
                        y1,
                        z1),
                    Color.Red));

                vertices.Add(new VertexPositionColor(
                    new Vector3(
                        xWorld,
                        y2,
                        z2),
                    Color.Red));
            }
        }

        DrawLines(
            vertices.ToArray(),
            view,
            projection);
    }

    public void DrawGameGrid(
        GameWorld gameMap,
        Matrix view,
        Matrix projection)
    {
        var vertices = new List<VertexPositionColor>();
        GameGrid grid = gameMap.GameGrid;
        Terrain terrain = gameMap.Terrain;

        const float heightOffset = 0.06f;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid.IsOccupied(x, y))
                    continue;

                float left = x * grid.CellSize;
                float right = left + grid.CellSize;
                float top = y * grid.CellSize;
                float bottom = top + grid.CellSize;

                Vector3 topLeft = new(left, terrain.GetHeight(x, y) + heightOffset, top);
                Vector3 topRight = new(right, terrain.GetHeight(x + 1, y) + heightOffset, top);
                Vector3 bottomLeft = new(left, terrain.GetHeight(x, y + 1) + heightOffset, bottom);
                Vector3 bottomRight = new(right, terrain.GetHeight(x + 1, y + 1) + heightOffset, bottom);

                vertices.Add(new VertexPositionColor(topLeft, Color.OrangeRed));
                vertices.Add(new VertexPositionColor(bottomRight, Color.OrangeRed));
                vertices.Add(new VertexPositionColor(topRight, Color.OrangeRed));
                vertices.Add(new VertexPositionColor(topLeft, Color.OrangeRed));
                vertices.Add(new VertexPositionColor(bottomLeft, Color.OrangeRed));
                vertices.Add(new VertexPositionColor(bottomRight, Color.OrangeRed));
            }
        }

        if (vertices.Count == 0)
            return;

        _effect.World = Matrix.Identity;
        _effect.View = view;
        _effect.Projection = projection;

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            Globals.GraphicsDevice.DrawUserPrimitives(
                PrimitiveType.TriangleList,
                vertices.ToArray(),
                0,
                vertices.Count / 3);
        }
    }
}
