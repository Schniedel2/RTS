using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>Streaming sector summaries; no candidate-cell lists or sorted group arrays.</summary>
public sealed class ScoutingCandidateSearch
{
    private readonly Dictionary<Point, Summary> _sectors = [];
    private struct Summary { public int Count; public Point Candidate; }
    public int ExaminedCells { get; private set; }
    public int StoredSectorCount => _sectors.Count;
    public Point? Target { get; private set; }

    public IEnumerable<int> Work(GameWorld world, MobileUnit unit, Guid army, Point current,
        Func<Point, bool> excluded, Func<Point, int, float> score, Random? random = null, Func<Point, bool>? reserve = null)
    {
        _sectors.Clear(); ExaminedCells = 0; Target = null;
        random ??= Random.Shared;
        int min = Math.Max(4, unit.GetSightRange() / 2), max = Math.Max(12, unit.GetSightRange() * 3);
        for (int z = Math.Max(0, current.Y - max); z <= Math.Min(world.GameGrid.Height - 1, current.Y + max); z++)
        for (int x = Math.Max(0, current.X - max); x <= Math.Min(world.GameGrid.Width - 1, current.X + max); x++)
        {
            yield return 0;
            ExaminedCells++;
            Point cell = new(x, z), sector = ScoutingTargets.Sector(cell);
            int distance = Math.Max(Math.Abs(x - current.X), Math.Abs(z - current.Y));
            if (distance < min || excluded(sector) ||
                world.Visibility.GetSimulationVisibility(army, cell) != VisibilityState.Unexplored ||
                (unit is not Helicopter && !unit.MovementProfile.CanUseTerrain(world.GameGrid.GetCell(cell)))) continue;
            _sectors.TryGetValue(sector, out Summary summary);
            summary.Count++;
            // Reservoir sampling preserves uniform choice without retaining all cells.
            if (random.Next(summary.Count) == 0) summary.Candidate = cell;
            _sectors[sector] = summary;
        }
        while (_sectors.Count > 0)
        {
            float best = float.NegativeInfinity;
            Target = null;
            foreach (var pair in _sectors)
            {
                yield return 0;
                if (excluded(pair.Key) || world.ScoutingTargets.Reserved(army, pair.Key, unit.UnitId)) continue;
                float value = score(pair.Key, pair.Value.Count);
                if (value > best) { best = value; Target = pair.Value.Candidate; }
            }
            if (Target is not Point target) yield break;
            // Reject stale samples or competing reservations without rescanning all cells.
            if (world.Visibility.GetSimulationVisibility(army, target) == VisibilityState.Unexplored &&
                (unit is Helicopter || unit.MovementProfile.CanUseTerrain(world.GameGrid.GetCell(target))) &&
                (reserve is null || reserve(target))) yield break;
            _sectors.Remove(ScoutingTargets.Sector(target));
            Target = null;
        }
    }
}
