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
        Point start =
            ToCell(unit.Position);

        Point destination =
            ToCell(
                new Vector3(
                    target.X,
                    0.0f,
                    target.Y));

        path = [];

        if (!movementProfile.CanEnter(
                _map,
                unit,
                start) ||
            !movementProfile.CanEnter(
                _map,
                unit,
                destination))
        {
            return false;
        }

        PriorityQueue<Point, float> openCells = new();

        Dictionary<Point, Point> previousCells = [];

        Dictionary<Point, float> pathCosts = [];

        pathCosts[start] = 0.0f;

        openCells.Enqueue(
            start,
            Heuristic(start, destination));

        while (openCells.Count > 0)
        {
            Point current =
                openCells.Dequeue();

            if (current == destination)
            {
                BuildPath(
                    start,
                    destination,
                    previousCells,
                    path);

                return true;
            }

            foreach (Point direction in Directions)
            {
                Point next =
                    current + direction;

                if (!movementProfile.CanEnter(
                        _map,
                        unit,
                        next))
                {
                    continue;
                }

                float movementCost =
                    movementProfile.GetMovementCost(
                        _map,
                        unit,
                        current,
                        next);

                float newCost =
                    pathCosts[current] +
                    movementCost;

                if (pathCosts.TryGetValue(
                        next,
                        out float oldCost) &&
                    newCost >= oldCost)
                {
                    continue;
                }

                pathCosts[next] = newCost;

                previousCells[next] =
                    current;

                float priority =
                    newCost +
                    Heuristic(
                        next,
                        destination);

                openCells.Enqueue(
                    next,
                    priority);
            }
        }

        return false;
    }

    //  Dijkstra
    public bool TryFindPath_Dijkstra(
        MobileUnit unit,
        IMovementProfile movementProfile,
        Vector2 target,
        out List<Point> path)
    {
        Point start = ToCell(unit.Position);
        Point destination = ToCell(new Vector3(target.X, 0.0f, target.Y));
        path = [];

        if (!movementProfile.CanEnter(_map, unit, start) ||
            !movementProfile.CanEnter(_map, unit, destination))
            return false;

        PriorityQueue<Point, float> openCells = new();
        Dictionary<Point, Point> previousCells = [];
        Dictionary<Point, float> pathCosts = [];
        HashSet<Point> visitedCells = [start];
        pathCosts.Add(start, 0.0f);
        openCells.Enqueue(start, 0.0f);

        while (openCells.Count > 0)
        {
            Point current = openCells.Dequeue();

            if (current == destination)
            {
                BuildPath(start, destination, previousCells, path);
                return true;
            }

            foreach (Point direction in Directions)
            {
                Point next = current + direction;

                if (visitedCells.Contains(next) ||
                        !movementProfile.CanEnter(_map, unit, next))
                    continue;

                visitedCells.Add(next);
                previousCells.Add(next, current);
                    float pathCost = pathCosts[current] +
                        movementProfile.GetMovementCost(_map, unit, current, next);
                    pathCosts.Add(next, pathCost);
                    openCells.Enqueue(next, pathCost);
            }
        }

        return false;
    }

    private Point ToCell(Vector3 position)
    {
        float cellSize = Globals.World.GameGrid.CellSize;

        return new Point(
            (int)(position.X / cellSize),
            (int)(position.Z / cellSize));
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