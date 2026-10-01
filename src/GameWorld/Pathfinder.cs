using System;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace RTS;

public class Pathfinder
{
    public const int MaximumExpandedNodes = 25000;
    private static readonly Point[] Directions =
    [
        new Point(1, 0),
        new Point(-1, 0),
        new Point(0, 1),
        new Point(0, -1),
        new Point(1, 1),
        new Point(-1, -1),
        new Point(-1, 1),
        new Point(1, -1),
    ];

    private readonly GameWorld _map;

    public Pathfinder(GameWorld map)
    {
        _map = map;
    }

    public bool TryFindPath(
        MobileUnit unit,
        IMovementProfile movementProfile,
        Vector2 target,
        out List<Point> path)
    {
        return TryFindPath_AStar(unit, movementProfile, target, out path);
    }

    public bool TryFindPathFrom(MobileUnit unit, IMovementProfile profile, Point start, Vector2 target, out List<Point> path) =>
        FindPath(unit, profile, target, true, out path, start);

    public bool TryFindPath_AStar(
        MobileUnit unit,
        IMovementProfile movementProfile,
        Vector2 target,
        out List<Point> path)
    {
        return FindPath(unit, movementProfile, target, true, out path);
    }

    public bool TryFindPath_Dijkstra(MobileUnit unit, IMovementProfile movementProfile, Vector2 target, out List<Point> path) =>
        FindPath(unit, movementProfile, target, false, out path);

    private bool FindPath(MobileUnit unit, IMovementProfile profile, Vector2 target, bool useHeuristic, out List<Point> path, Point? startOverride = null)
    {
        using var measurement = PerformanceMeasurements.Measure("Pathfinder.Search");
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return FindPathCore(unit, profile, target, useHeuristic, out path, startOverride);
        }
        finally
        {
            Globals.Telemetry.Pathfinding_Last = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Globals.Telemetry.Pathfinding_Total += Globals.Telemetry.Pathfinding_Last;
            Globals.Telemetry.Pathfinding_Avg = Globals.Telemetry.Pathfinding_Total / Globals.Telemetry.TryFindPath_Calls;
        }
    }

    private bool FindPathCore(MobileUnit unit, IMovementProfile profile, Vector2 target, bool useHeuristic, out List<Point> path, Point? startOverride)
    {
        Search search = CreateSearch(unit, profile, startOverride ?? _map.GameGrid.ToCell(unit.Position), target, useHeuristic);
        foreach (int step in search.Work()) { }
        path = search.Path;
        return search.Succeeded;
    }

    public Search CreateSearch(MobileUnit unit, IMovementProfile profile, Point start, Vector2 target, bool useHeuristic = true) =>
        new(_map, unit, profile, start, target, useHeuristic);

    public sealed class Search(GameWorld map, MobileUnit unit, IMovementProfile profile, Point initialStart, Vector2 target, bool useHeuristic)
    {
        public List<Point> Path { get; } = [];
        public bool Succeeded { get; private set; }
        public bool Complete { get; private set; }
        public int ExpandedNodes { get; private set; }
        public int Restarts { get; private set; }
        private bool _invalidated;
        private long _revision;

        public IEnumerable<int> Work()
        {
            Globals.Telemetry.TryFindPath_Calls++;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _invalidated = false;
                Succeeded = false;
                Path.Clear();
                _revision = map.GameGrid.NavigationRevision;
                foreach (int step in Run()) yield return step;
                if (!_invalidated) break;
                Restarts++;
            }
            if (_invalidated) { Succeeded = false; Path.Clear(); }
            Complete = true;
        }

        private IEnumerable<int> Run()
        {
            GameGrid grid = map.GameGrid;
            Point start = initialStart;
            Point destination = grid.ToCell(new Vector3(target.X, 0, target.Y));
            List<Point> path = Path;
            yield return 0;
            if (!grid.Contains(start) || !profile.CanEnter(map, unit, start))
                yield break;
            if (start == destination)
            { Succeeded = true; yield break; }
            if (!profile.CanEnter(map, unit, destination) || !grid.IsPathfindingAllowed(unit, destination))
                yield break;

            // Search-local caches are discarded when static navigation changes.
            HashSet<Point> startingFootprint = grid.GetPathfindingStartingFootprint(unit, start);
            Dictionary<Point, bool> passability = [];
            Dictionary<Point, bool> finalPassability = [];
            Dictionary<Point, float> terrainCosts = [];
            bool standardGroundProfile = profile.GetType() == typeof(GroundMovementProfile);
            PriorityQueue<(Point Cell, float Cost), float> open = new();
            Dictionary<Point, Point> previous = [];
            Dictionary<Point, float> costs = new() { [start] = 0 };
            open.Enqueue((start, 0), 0);
            int expandedNodes = 0;
            while (open.TryDequeue(out var item, out _))
            {
                yield return 0;
                if (grid.NavigationRevision != _revision) { _invalidated = true; yield break; }
                ExpandedNodes++;
                if (++expandedNodes > MaximumExpandedNodes)
                    yield break;
                Point current = item.Cell;
                if (item.Cost > costs[current])
                    continue;
                if (current == destination)
                {
                    Point cursor = destination;
                    while (cursor != start)
                    {
                        yield return 0;
                        if (grid.NavigationRevision != _revision) { _invalidated = true; yield break; }
                        path.Add(cursor);
                        cursor = previous[cursor];
                    }
                    path.Reverse();
                    // Mobile occupancy changes do not restart every search. Check the
                    // completed corridor against the current world before accepting it.
                    Point from = start;
                    foreach (Point cell in path)
                    {
                        yield return 0;
                        if (grid.NavigationRevision != _revision || !CanEnterNow(cell) ||
                            (cell.X != from.X && cell.Y != from.Y &&
                             (!CanEnterNow(new Point(cell.X, from.Y)) || !CanEnterNow(new Point(from.X, cell.Y)))))
                        { _invalidated = true; yield break; }
                        from = cell;
                    }
                    Succeeded = true;
                    yield break;
                }
                foreach (Point direction in Directions)
                {
                    Point next = current + direction;
                    if (!CanPlan(next))
                        continue;
                    if (direction.X != 0 && direction.Y != 0 &&
                        (!CanPlan(new Point(current.X + direction.X, current.Y)) ||
                         !CanPlan(new Point(current.X, current.Y + direction.Y))))
                        continue;
                    float stepCost;
                    if (standardGroundProfile)
                    {
                        if (!terrainCosts.TryGetValue(next, out float terrainCost))
                            terrainCosts[next] = terrainCost = grid.GetMovementCost(unit, next);
                        stepCost = terrainCost * (direction.X != 0 && direction.Y != 0 ? 1.4142135f : 1.0f);
                    }
                    else
                        stepCost = profile.GetMovementCost(map, unit, current, next);
                    if (!float.IsFinite(stepCost) || stepCost <= 0)
                        throw new InvalidOperationException("Movement costs must be finite and positive.");
                    float candidate = item.Cost + stepCost;
                    if (costs.TryGetValue(next, out float known) && candidate >= known)
                        continue;
                    costs[next] = candidate;
                    previous[next] = current;
                    open.Enqueue((next, candidate), candidate + (useHeuristic ? Heuristic(next, destination) : 0));
                }
            }
            yield break;

            // Allow the entire initial footprint to leave a planning exclusion after spawning.
            bool CanPlan(Point cell)
            {
                if (!passability.TryGetValue(cell, out bool allowed))
                    passability[cell] = allowed = grid.Contains(cell) && profile.CanEnter(map, unit, cell) &&
                        grid.IsPathfindingAllowedFromFootprint(unit, cell, startingFootprint);
                return allowed;
            }
            bool CanEnterNow(Point cell)
            {
                if (!finalPassability.TryGetValue(cell, out bool allowed))
                    finalPassability[cell] = allowed = grid.Contains(cell) && profile.CanEnter(map, unit, cell) &&
                        grid.IsPathfindingAllowedFromFootprint(unit, cell, startingFootprint);
                return allowed;
            }
        }
    }

    private static float Heuristic(Point a, Point b)
    {
        int dx = Math.Abs(a.X - b.X);
        int dy = Math.Abs(a.Y - b.Y);

        int diagonal = Math.Min(dx, dy);
        int straight = Math.Max(dx, dy) - diagonal;

        return diagonal * 1.4142135f + straight;
    }    
}
