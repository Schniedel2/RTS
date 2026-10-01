using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public class PathfindingManager
{
    private readonly Pathfinder _pathfinder;
    private readonly GameWorld _map;
    private long _sessionGeneration = -1;
    private Network.NetworkHandler? _network;

    private readonly Queue<PathRequest> _requests = [];

    public int PendingRequests =>
        _requests.Count + Scheduler.PendingJobs;

    public PathfindingManager(GameWorld map)
    {
        _map = map;
        _pathfinder = new Pathfinder(map);
    }

    public void RequestPath(
        MobileUnit unit,
        IMovementProfile movementProfile,
        Vector2 target,
        int pathRequestId)
    {
        if (!MobileUnit.IsMovementAuthority) return;
        EnsureSession();
        Debug($"request unit={ShortId(unit.UnitId)} target=({target.X:0.0},{target.Y:0.0}) request={pathRequestId}");
        _requests.Enqueue(
            new PathRequest(
                unit,
                movementProfile,
                target,
                pathRequestId));
    }

    public PlanningScheduler Scheduler { get; } = new();
    public Pathfinder.Search CreateSearch(MobileUnit unit, Point start, Vector2 target) =>
        _pathfinder.CreateSearch(unit, unit.MovementProfile, start, target);

    public void Reset()
    {
        while (_requests.TryDequeue(out PathRequest request))
            if (request.Unit._pathRequestId == request.PathRequestId && request.Unit.CurrentCommand?.Target == request.Target)
                request.Unit.OnPathSearchFailed();
        Scheduler.Reset();
        _network = Globals.Game?.Network;
        _sessionGeneration = _network?.SessionGeneration ?? 0;
    }

    private void EnsureSession()
    {
        Network.NetworkHandler? network = Globals.Game?.Network;
        if (!ReferenceEquals(_network, network) || (network?.SessionGeneration ?? 0) != _sessionGeneration)
        {
            if (_network is not null) Reset();
            _network = network;
            _sessionGeneration = network?.SessionGeneration ?? 0;
        }
    }

    public void Update(int maximumSteps = PlanningScheduler.MaximumStepsPerUpdate,
        double maximumMilliseconds = PlanningScheduler.MaximumMillisecondsPerUpdate)
    {
        EnsureSession();
        if (!MobileUnit.IsMovementAuthority) { Reset(); return; }
        for (int count = 0; count < 64 && _requests.TryDequeue(out PathRequest request); count++)
        {
            MobileUnit unit = request.Unit;
            bool Valid() => unit._pathRequestId == request.PathRequestId && !unit.IsDying && !unit.IsEmbarked &&
                unit.CurrentCommand?.Target == request.Target && _map.GameGrid.IsRegistered(unit);
            if (!Valid()) continue;
            Point start = _map.GameGrid.ToCell(unit.Position);
            Pathfinder.Search search = _pathfinder.CreateSearch(unit, request.MovementProfile, start, request.Target);
            Scheduler.Enqueue(search.Work(), Valid, () =>
            {
                if (search.Succeeded && _map.GameGrid.ToCell(unit.Position) == start) unit.SetPlannedPath(search.Path);
                else unit.OnPathSearchFailed();
            }, () => { if (Valid()) unit.OnPathSearchFailed(); });
        }
        Scheduler.Update(maximumSteps, maximumMilliseconds);
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
        // Synchronous compatibility for diagnostics. Runtime gameplay enqueues
        // CreateSearch(...).Work() into Scheduler instead.
        path = [];
        return MobileUnit.IsMovementAuthority &&
            _pathfinder.TryFindPathFrom(unit, unit.MovementProfile, start, target, out path);
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
