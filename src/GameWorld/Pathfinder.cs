using System;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace RTS;

public class Pathfinder
{
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
        DateTime t0 = DateTime.Now;
        
        bool result = TryFindPath_AStar(
            unit,
            movementProfile,
            target,
            out path);

        Globals.Telemetry.Pathfinding_Last = (DateTime.Now - t0).TotalMilliseconds;
        Globals.Telemetry.Pathfinding_Total += Globals.Telemetry.Pathfinding_Last;
        Globals.Telemetry.TryFindPath_Calls++;
        Globals.Telemetry.Pathfinding_Avg = Globals.Telemetry.TryFindPath_Calls == 0 ? 0.0 : Globals.Telemetry.Pathfinding_Total / Globals.Telemetry.TryFindPath_Calls;
        return result;
    }

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

    private bool FindPath(MobileUnit unit, IMovementProfile profile, Vector2 target, bool useHeuristic, out List<Point> path)
    {
        GameGrid grid = _map.GameGrid;
        Point start = grid.ToCell(unit.Position);
        Point destination = grid.ToCell(new Vector3(target.X, 0, target.Y));
        path = [];
        if (!grid.Contains(start) || !profile.CanEnter(_map, unit, start))
            return false;
        if (start == destination)
            return true;
        if (!profile.CanEnter(_map, unit, destination) || !grid.IsPathfindingAllowed(unit, destination))
            return false;

        PriorityQueue<(Point Cell, float Cost), float> open = new();
        Dictionary<Point, Point> previous = [];
        Dictionary<Point, float> costs = new() { [start] = 0 };
        open.Enqueue((start, 0), 0);
        while (open.TryDequeue(out var item, out _))
        {
            Point current = item.Cell;
            if (item.Cost > costs[current])
                continue;
            if (current == destination)
            {
                BuildPath(start, destination, previous, path);
                return true;
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
                float stepCost = profile.GetMovementCost(_map, unit, current, next);
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
        return false;

        // Allow the entire initial footprint to leave a planning exclusion after spawning.
        bool CanPlan(Point cell) => profile.CanEnter(_map, unit, cell) &&
            grid.IsPathfindingAllowed(unit, cell, start);
    }

    private static void BuildPath(
        Point start,
        Point destination,
        IReadOnlyDictionary<Point, Point> previousCells,
        List<Point> path)
    {
        Point current = destination;

        while (current != start)
        {
            path.Add(current);
            current = previousCells[current];
        }

        path.Reverse();
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