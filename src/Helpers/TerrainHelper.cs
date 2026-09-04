using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace RTS;

public static class TerrainHelper
{
    public static bool IsInsideMap(Terrain _terrain, int x, int z)
    {
        return x >= 0 && x < _terrain.Width && z >= 0 && z < _terrain.Height;
    }
    public static Point[] GetCells(Terrain _terrain, Vector2 mouseWorldPosition, ToolShape toolShape, int toolSize)
    {
        //  determine all terrain cells affected by the current tool
        //  this is a placeholder implementation
        List<Point> cells = new List<Point>();
        //  use mouseWorldPosition, toolShape, and toolSize to determine affected cells
        int x = (int)mouseWorldPosition.X;
        int y = (int)mouseWorldPosition.Y;

        Random rnd = new Random(5);
        for (int ry = y-(toolSize - 1); ry <= y+(toolSize - 1); ry++)
            for (int rx = x-(toolSize - 1); rx <= x+(toolSize - 1); rx++)
            {
                if (toolShape == ToolShape.Circle)
                {
                    int dx = rx - x;
                    int dy = ry - y;
                    if (dx * dx + dy * dy > (toolSize - 1) * (toolSize - 1))
                        continue;
                }
                if (toolShape == ToolShape.Dither)
                {
                    if (rnd.Next(100) > 5)
                        continue;
                }
                if (IsInsideMap(_terrain, rx, ry))
                    cells.Add(new Point(rx, ry));
            }
        return cells.ToArray();
    }

    public static void RaiseTerrain(Terrain _terrain, float x, float z, ToolShape toolShape, int toolSize, float amount)
    {
        Point[] affectedCells = GetCells(_terrain, new Vector2(x, z), toolShape, toolSize);
        foreach (Point cell in affectedCells)
        {
            float distance = Vector2.Distance(new Vector2(cell.X, cell.Y), new Vector2(x, z));
            float h = _terrain.GetHeight(cell.X, cell.Y);
            h += amount * MathF.Max(0, 1 - distance / toolSize);
            _terrain.SetHeight(cell.X, cell.Y, h);
        }
        _terrain.BuildMesh();
    }    

    public static void FlattenTerrain(Terrain _terrain, float x, float z, float targetHeight, ToolShape toolShape, int toolSize, float amount)
    {
        Point[] affectedCells = GetCells(_terrain, new Vector2(x, z), toolShape, toolSize);
        float averageHeight = targetHeight;
        foreach (Point cell in affectedCells)
        {
            float distance = Vector2.Distance(new Vector2(cell.X, cell.Y), new Vector2(x, z));
            float h = _terrain.GetHeight(cell.X, cell.Y);
            h += (averageHeight - h) * amount * MathF.Max(0, 1 - distance / toolSize);
            _terrain.SetHeight(cell.X, cell.Y, h);
        }
        _terrain.BuildMesh();
    }

    public static void SmoothTerrain(Terrain _terrain, float x, float z, ToolShape toolShape, int toolSize, float amount)
    {
        Point[] affectedCells = GetCells(_terrain, new Vector2(x, z), toolShape, toolSize);
        float totalHeihgt = 0;
        foreach (Point cell in affectedCells)
        {
            totalHeihgt += _terrain.GetHeight(cell.X, cell.Y);
        }
        float averageHeight = totalHeihgt / affectedCells.Length;
        foreach (Point cell in affectedCells)
        {
            float distance = Vector2.Distance(new Vector2(cell.X, cell.Y), new Vector2(x, z));
            float h = _terrain.GetHeight(cell.X, cell.Y);
            h += (averageHeight - h) * amount * MathF.Max(0, 1 - distance / toolSize);
            _terrain.SetHeight(cell.X, cell.Y, h);
        }
        _terrain.BuildMesh();
    }
    
    public static void SetTile(Terrain _terrain, float x, float z, ToolShape toolShape, int toolSize, TerrainTile terrainTile)
    {
        Point[] affectedCells = GetCells(_terrain, new Vector2(x, z), toolShape, toolSize);
        foreach (Point cell in affectedCells)
        {
            _terrain.SetTile(cell.X, cell.Y, terrainTile);
        }
        _terrain.UpdateTilemap();
    }    
    
}