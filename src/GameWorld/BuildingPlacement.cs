using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

[Flags]
public enum PlacementIssue { None = 0, OutsideTerrain = 1, Blocked = 2, Occupied = 4, UnevenTerrain = 8, Reserved = 16 }

public sealed record PlacementCell(Point Cell, PlacementIssue Issues, float MinimumHeight, float MaximumHeight, bool IsClearance = false);

/// <summary>One shared result for placement, host validation and the colored preview.</summary>
public sealed record BuildingPlacement(IReadOnlyList<PlacementCell> Cells, float HeightDifference)
{
    public bool IsAllowed => Cells.Count > 0 && Cells.All(cell => cell.Issues == PlacementIssue.None);

    public bool TryGetMovableBlockers(
        GameGrid grid,
        Func<MobileUnit, bool> canMove,
        out MobileUnit[] blockers)
    {
        HashSet<MobileUnit> result = [];
        foreach (PlacementCell cell in Cells)
        {
            if (cell.Issues == PlacementIssue.None) continue;
            if (!IsMovableBlocker(cell, grid, canMove))
            {
                blockers = [];
                return false;
            }
            result.Add((MobileUnit)grid.GetOccupant(cell.Cell)!);
        }
        blockers = result.ToArray();
        return blockers.Length > 0;
    }

    public static bool IsMovableBlocker(
        PlacementCell cell,
        GameGrid grid,
        Func<MobileUnit, bool> canMove) =>
        cell.Issues == PlacementIssue.Occupied &&
        grid.GetOccupant(cell.Cell) is MobileUnit mobile && canMove(mobile);

    public static BuildingPlacement Evaluate(GameWorld world, Unit unit, Vector3 position, float rotation, float tolerance)
    {
        if (!float.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        Terrain terrain = world.Terrain;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
            !float.IsFinite(rotation) || position.X < 0 || position.Z < 0 ||
            position.X >= terrain.Width - 1 || position.Z >= terrain.Height - 1)
            return new BuildingPlacement(Array.Empty<PlacementCell>(), 0);

        GameGrid grid = world.GameGrid;
        List<PlacementCell> cells = [];
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        HashSet<Point> footprint = grid.GetFootprintCells(unit, position, rotation).ToHashSet();
        foreach (Point cell in footprint)
        {
            int left = cell.X * grid.CellSize, top = cell.Y * grid.CellSize;
            if (!grid.Contains(cell) || left < 0 || top < 0 ||
                left + grid.CellSize >= terrain.Width || top + grid.CellSize >= terrain.Height)
            {
                cells.Add(new(cell, PlacementIssue.OutsideTerrain, 0, 0));
                continue;
            }
            PlacementIssue issues = grid.GetCell(cell).IsBlocked ? PlacementIssue.Blocked : PlacementIssue.None;
            if (grid.GetOccupant(cell) is Unit occupant && occupant != unit)
                issues |= PlacementIssue.Occupied;
            if (grid.IsReservedForBuilding(cell, unit))
                issues |= PlacementIssue.Reserved;
            float cellMinimum = float.PositiveInfinity, cellMaximum = float.NegativeInfinity;
            // Include shared boundary vertices and every interior vertex for coarse grids.
            for (int z = top; z <= top + grid.CellSize; z++)
                for (int x = left; x <= left + grid.CellSize; x++)
                {
                    float height = terrain.GetHeight(x, z);
                    cellMinimum = Math.Min(cellMinimum, height);
                    cellMaximum = Math.Max(cellMaximum, height);
                }
            if (!float.IsFinite(cellMinimum) || !float.IsFinite(cellMaximum))
                issues |= PlacementIssue.UnevenTerrain;
            minimum = Math.Min(minimum, cellMinimum);
            maximum = Math.Max(maximum, cellMaximum);
            cells.Add(new(cell, issues, cellMinimum, cellMaximum));
        }
        foreach (Point cell in grid.GetClearanceCells(unit, position, rotation).Where(cell => !footprint.Contains(cell)))
        {
            int left = cell.X * grid.CellSize, top = cell.Y * grid.CellSize;
            if (!grid.Contains(cell) || left < 0 || top < 0 ||
                left + grid.CellSize >= terrain.Width || top + grid.CellSize >= terrain.Height)
            {
                cells.Add(new(cell, PlacementIssue.OutsideTerrain, 0, 0, true));
                continue;
            }
            PlacementIssue issues = grid.GetCell(cell).IsBlocked ? PlacementIssue.Blocked : PlacementIssue.None;
            if (grid.GetOccupant(cell) is Unit occupant && occupant != unit)
                issues |= PlacementIssue.Occupied;
            cells.Add(new(cell, issues, 0, 0, true));
        }
        float difference = maximum >= minimum ? maximum - minimum : 0;
        if (difference > tolerance)
            for (int i = 0; i < cells.Count; i++)
            {
                PlacementCell cell = cells[i];
                // Mark cells whose vertices exceed the tolerance relative to
                // another part of the footprint, including both peaks and dips.
                if (!cell.Issues.HasFlag(PlacementIssue.OutsideTerrain) &&
                    (cell.MaximumHeight - minimum > tolerance || maximum - cell.MinimumHeight > tolerance))
                    cells[i] = cell with { Issues = cell.Issues | PlacementIssue.UnevenTerrain };
            }
        return new BuildingPlacement(cells, difference);
    }
}
