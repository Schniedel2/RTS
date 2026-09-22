using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum EarthworkKind { LevelAndConcrete, RemoveConcrete }
public sealed record EarthworkOrder(Guid Id, EarthworkKind Kind, int X, int Z, float TargetHeight)
{
    public bool IsDrive { get; init; }
    public float StartX { get; init; }
    public float StartZ { get; init; }
    public float DestinationX { get; init; }
    public float DestinationZ { get; init; }
    public bool Contains(Point cell) => IsDrive || Area.Contains(cell);
    public const int Size = 8;
    public Rectangle Area => new(X, Z, Size, Size);
}
public sealed record EarthworkCell(Point Cell, bool Allowed, bool NeedsWork);
public sealed record EarthworkPreview(EarthworkOrder Order, IReadOnlyList<EarthworkCell> Cells)
{
    public bool IsAllowed => Order.IsDrive ? Cells.Any(c => c.Allowed) : Cells.Count == 64 && Cells.All(c => c.Allowed) && Cells.Any(c => c.NeedsWork);
}

public static class Earthwork
{
    public const float MaximumHeightChange = 2.0f;
    public const float SecondsPerCell = 0.1f;

    public static EarthworkPreview Preview(GameWorld world, GDIBulldozer worker, Point center, EarthworkKind kind)
    {
        Point current = world.GameGrid.ToCell(worker.Position);
        Vector3 reference = world.GameGrid.ToWorldPosition(current, 0);
        float height = world.Terrain.GetSurfaceHeight(reference.X, reference.Z);
        var order = new EarthworkOrder(Guid.NewGuid(), kind, center.X - 4, center.Y - 4, height);
        if (kind == EarthworkKind.LevelAndConcrete)
        {
            Vector3 destination = world.GameGrid.ToWorldPosition(center, 0);
            order = order with { IsDrive = true, StartX = worker.Position.X, StartZ = worker.Position.Z,
                DestinationX = destination.X, DestinationZ = destination.Z };
            List<EarthworkCell> cells = [];
            HashSet<Point> seen = [];
            Vector2 start = new(order.StartX, order.StartZ), end = new(order.DestinationX, order.DestinationZ);
            int steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(start, end) / (world.GameGrid.CellSize * 0.25f)));
            bool blocked = false;
            for (int i = 0; i <= steps; i++)
            {
                var section = DriveCells(world, worker, order, Vector2.Lerp(start, end, (float)i / steps));
                blocked |= section.Any(c => !CanWork(world, worker, order, c));
                foreach (Point c in section)
                    if (seen.Add(c)) cells.Add(new(c, !blocked, NeedsWork(world, order, c)));
            }
            return new(order, cells);
        }
        return Evaluate(world, worker, order);
    }

    public static EarthworkPreview Evaluate(GameWorld world, GDIBulldozer worker, EarthworkOrder order)
    {
        List<EarthworkCell> cells = [];
        for (int z = order.Z; z < order.Z + EarthworkOrder.Size; z++)
            for (int x = order.X; x < order.X + EarthworkOrder.Size; x++)
            {
                Point cell = new(x, z);
                cells.Add(new(cell, CanWork(world, worker, order, cell), NeedsWork(world, order, cell)));
            }
        return new(order, cells);
    }

    public static List<Point> DriveCells(GameWorld world, GDIBulldozer worker, EarthworkOrder order, Vector2 position)
    {
        Vector2 forward = new(order.DestinationX - order.StartX, order.DestinationZ - order.StartZ);
        if (forward.LengthSquared() < 0.0001f) forward = new(0, -1);
        else forward.Normalize();
        Vector2 right = new(-forward.Y, forward.X);
        float size = world.GameGrid.CellSize;
        float w = worker.Width * size * 0.5f, l = worker.Length * size * 0.5f;
        position += right * worker.FootprintLocalCenter.X - forward * worker.FootprintLocalCenter.Z;
        float ex = Math.Abs(right.X) * w + Math.Abs(forward.X) * l;
        float ez = Math.Abs(right.Y) * w + Math.Abs(forward.Y) * l;
        float h = size * 0.5f;
        List<Point> cells = [];
        for (int z = (int)MathF.Floor((position.Y - ez) / size); z <= (int)MathF.Floor((position.Y + ez) / size); z++)
            for (int x = (int)MathF.Floor((position.X - ex) / size); x <= (int)MathF.Floor((position.X + ex) / size); x++)
            {
                Vector2 d = new Vector2((x + 0.5f) * size, (z + 0.5f) * size) - position;
                if (Math.Abs(d.X) >= ex + h - 0.0001f || Math.Abs(d.Y) >= ez + h - 0.0001f ||
                    Math.Abs(Vector2.Dot(d, right)) >= w + h * (Math.Abs(right.X) + Math.Abs(right.Y)) - 0.0001f ||
                    Math.Abs(Vector2.Dot(d, forward)) >= l + h * (Math.Abs(forward.X) + Math.Abs(forward.Y)) - 0.0001f) continue;
                cells.Add(new(x, z));
            }
        return cells;
    }

    public static bool CanWork(GameWorld world, GDIBulldozer worker, EarthworkOrder order, Point cell)
    {
        GameGrid grid = world.GameGrid;
        if (!grid.Contains(cell) || !grid.GetCell(cell).HasTerrain || grid.GetCell(cell).IsBlocked ||
            grid.GetCell(cell).ExcludeFromPathfinding || !float.IsFinite(order.TargetHeight))
            return false;
        if (order.IsDrive && (grid.GetCell(cell).AllowedMovement & MovementModes.Drive) == 0) return false;
        if (!order.IsDrive && !NeedsWork(world, order, cell)) return true;
        bool leveling = order.Kind == EarthworkKind.LevelAndConcrete;
        int border = leveling ? 1 : 0;
        // A changed corner also belongs to the eight neighboring terrain cells.
        for (int z = cell.Y - border; z <= cell.Y + border; z++)
            for (int x = cell.X - border; x <= cell.X + border; x++)
            {
                Point affected = new(x, z);
                if (!grid.Contains(affected)) continue;
                if (grid.GetOccupant(affected) is Unit occupant && occupant != worker)
                    return false;
                if (leveling && grid.GetCell(affected).IsBlocked)
                    return false;
            }
        if (leveling)
            foreach (Point vertex in Vertices(grid, cell))
                if (Math.Abs(world.Terrain.GetHeight(vertex.X, vertex.Y) - order.TargetHeight) > MaximumHeightChange)
                    return false;
        return true;
    }

    public static bool NeedsWork(GameWorld world, EarthworkOrder order, Point cell)
    {
        if (!world.GameGrid.Contains(cell) || !world.GameGrid.GetCell(cell).HasTerrain) return false;
        int size = world.GameGrid.CellSize;
        for (int z = cell.Y * size; z < (cell.Y + 1) * size; z++)
            for (int x = cell.X * size; x < (cell.X + 1) * size; x++)
                if (order.Kind == EarthworkKind.RemoveConcrete
                    ? world.Terrain.GetTile(x, z) == TerrainTile.Concrete
                    : world.Terrain.GetTile(x, z) != TerrainTile.Concrete)
                    return true;
        return order.Kind == EarthworkKind.LevelAndConcrete && Vertices(world.GameGrid, cell)
            .Any(v => Math.Abs(world.Terrain.GetHeight(v.X, v.Y) - order.TargetHeight) > 0.001f);
    }

    public static float Duration(GameWorld world, EarthworkOrder order, Point cell) => SecondsPerCell +
        (order.Kind == EarthworkKind.LevelAndConcrete
            ? 2 * Vertices(world.GameGrid, cell).Max(v => Math.Abs(world.Terrain.GetHeight(v.X, v.Y) - order.TargetHeight)) : 0);

    public static IEnumerable<Point> Vertices(GameGrid grid, Point cell)
    {
        for (int z = cell.Y * grid.CellSize; z <= (cell.Y + 1) * grid.CellSize; z++)
            for (int x = cell.X * grid.CellSize; x <= (cell.X + 1) * grid.CellSize; x++)
                yield return new(x, z);
    }

    public static void ApplyCell(GameWorld world, EarthworkOrder order, Point cell, bool refreshGraphics = true)
    {
        if (!order.Contains(cell) || !world.GameGrid.Contains(cell) || !world.GameGrid.GetCell(cell).HasTerrain)
            return;
        if (order.Kind == EarthworkKind.LevelAndConcrete)
            foreach (Point vertex in Vertices(world.GameGrid, cell))
                world.Terrain.SetHeight(vertex.X, vertex.Y, order.TargetHeight);
        int size = world.GameGrid.CellSize;
        for (int z = cell.Y * size; z < (cell.Y + 1) * size; z++)
            for (int x = cell.X * size; x < (cell.X + 1) * size; x++)
                if (order.Kind == EarthworkKind.LevelAndConcrete || world.Terrain.GetTile(x, z) == TerrainTile.Concrete)
                    world.Terrain.SetTile(x, z, order.Kind == EarthworkKind.LevelAndConcrete ? TerrainTile.Concrete : TerrainTile.Dirt);
        if (refreshGraphics)
        {
            if (order.Kind == EarthworkKind.LevelAndConcrete) world.Terrain.BuildTerrainMesh();
            world.Terrain.UpdateTilemapTexture();
        }
    }
}
