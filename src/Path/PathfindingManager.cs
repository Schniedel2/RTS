using Microsoft.Xna.Framework;
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
        _requests.Enqueue(
            new PathRequest(
                unit,
                movementProfile,
                target,
                pathRequestId));
    }

    public void Update()
    {
        while (!UpdateRequest());
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
            return true; // force next call to process the next request

        // Unit könnte inzwischen einen neuen
        // Befehl bekommen haben.
        if (unit.CurrentCommand == null)
            return false;

        if (!unit.CurrentCommand.Value.Target.Equals(
                request.Target))
        {
            return true;
        }

        if (_pathfinder.TryFindPath(
                unit,
                request.MovementProfile,
                request.Target,
                out List<Point> path))
        {
            if (unit._pathRequestId == request.PathRequestId)
            unit.SetPlannedPath(path);
        }
        else
        {
            unit.ClearCommand();
        }

        return true;
    }

    private readonly record struct PathRequest(
        MobileUnit Unit,
        IMovementProfile MovementProfile,
        Vector2 Target,
        int PathRequestId);
}