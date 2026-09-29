using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public class PathfindingManager
{
    private readonly Pathfinder _pathfinder;

    private readonly Queue<PathRequest> _requests = [];

    public int PendingRequests =>
        _requests.Count;

    public PathfindingManager(GameWorld map)
    {
        _pathfinder = new Pathfinder(map);
    }

    public void RequestPath(
        MobileUnit unit,
        IMovementProfile movementProfile,
        Vector2 target,
        int pathRequestId)
    {
        if (!MobileUnit.IsMovementAuthority) return;
        Debug($"request unit={ShortId(unit.UnitId)} target=({target.X:0.0},{target.Y:0.0}) request={pathRequestId}");
        _requests.Enqueue(
            new PathRequest(
                unit,
                movementProfile,
                target,
                pathRequestId));
    }

    public void Update()
    {
        if (!MobileUnit.IsMovementAuthority)
        {
            _requests.Clear();
            return;
        }
        // Discard stale work cheaply, but do not let a cancelled backlog consume
        // an entire frame. UpdateRequest stops after one real path search.
        for (int discarded = 0; discarded < 64; discarded++)
            if (UpdateRequest()) break;
    }

    bool UpdateRequest()
    {
        // Zunächst absichtlich nur EINEN
        // Pathfinding-Auftrag pro Frame bearbeiten.

        if (_requests.Count == 0)
            return true;

        PathRequest request = _requests.Dequeue();
        MobileUnit unit = request.Unit;

        if (unit._pathRequestId != request.PathRequestId)
        {
            Debug($"discard stale request unit={ShortId(unit.UnitId)} request={request.PathRequestId} current={unit._pathRequestId}");
            return false;
        }

        // Unit könnte inzwischen einen neuen
        // Befehl bekommen haben.
        if (unit.CurrentCommand == null)
        {
            Debug($"discard cancelled request unit={ShortId(unit.UnitId)} request={request.PathRequestId}");
            return false;
        }

        if (!unit.CurrentCommand.Value.Target.Equals(
                request.Target))
        {
            Debug($"discard replaced request unit={ShortId(unit.UnitId)} request={request.PathRequestId}");
            return false;
        }

        if (_pathfinder.TryFindPath(
                unit,
                request.MovementProfile,
                request.Target,
                out List<Point> path))
        {
            if (unit._pathRequestId == request.PathRequestId)
            {
                Debug($"path found unit={ShortId(unit.UnitId)} request={request.PathRequestId} waypoints={path.Count}");
                unit.SetPlannedPath(path);
            }
        }
        else
        {
            Debug($"path failed unit={ShortId(unit.UnitId)} request={request.PathRequestId}; waiting before retry");
            unit.OnPathSearchFailed();
        }

        return true;
    }

    private readonly record struct PathRequest(
        MobileUnit Unit,
        IMovementProfile MovementProfile,
        Vector2 Target,
        int PathRequestId);

    private static void Debug(string message)
    {
        if (Globals.Debug_ShowPathfindingMessages)
            Globals.Console.Print($"[PATH] {message}");
    }

    public bool TryFindPath(MobileUnit unit, Point start, Vector2 target, out List<Point> path)
    {
        path = [];
        return MobileUnit.IsMovementAuthority &&
            _pathfinder.TryFindPathFrom(unit, unit.MovementProfile, start, target, out path);
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
