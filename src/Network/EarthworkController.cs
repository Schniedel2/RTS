using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS.Network;

/// <summary>Host-only scheduler. Clients receive movement orders and absolute cell results.</summary>
public sealed class EarthworkController(GameWorld world, Guid hostId, Action<NetworkMessage> publish)
{
    private sealed class Job(GDIBulldozer worker, Guid player, EarthworkOrder order, List<Point> cells)
    {
        public GDIBulldozer Worker = worker;
        public Guid Player = player;
        public EarthworkOrder Order = order;
        public List<Point> Cells = cells;
        public int Index, Sequence;
        public float WorkTime, RetryTime, StalledTime;
        public Vector3 LastPosition = worker.Position;
        public Point? Approach;
    }
    private readonly Dictionary<Guid, Job> _jobs = [];
    public int ActiveJobs => _jobs.Count;

    public NetworkMessage? Start(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || request.EarthworkKind is not EarthworkKind kind || !Enum.IsDefined(kind) ||
            world.Units.FindById(id) is not GDIBulldozer worker || worker.IsDying || worker.IsEmbarked ||
            worker.Occupancy?.IsOperational == false || !Globals.Game.Armies.CanControl(request.SenderId, worker.ArmyId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Z) || request.X < 0 || request.Z < 0 ||
            request.X >= world.Terrain.Width - 1 || request.Z >= world.Terrain.Height - 1)
            return null;
        var preview = Earthwork.Preview(world, worker, world.GameGrid.ToCell(new Vector3(request.X, 0, request.Z)), kind);
        if (!preview.IsAllowed) return null;
        Rectangle area = preview.Order.Area;
        area.Inflate(1, 1);
        if (_jobs.Values.Any(j => j.Worker != worker && area.Intersects(j.Order.Area))) return null;
        List<Point> cells = [];
        for (int row = 0; row < 8; row++)
            for (int column = 0; column < 8; column++)
                cells.Add(new(preview.Order.X + (row % 2 == 0 ? column : 7 - column), preview.Order.Z + row));
        Vector3 first = world.GameGrid.ToWorldPosition(cells[0], 0), last = world.GameGrid.ToWorldPosition(cells[^1], 0);
        if (Vector2.DistanceSquared(new(first.X, first.Z), new(worker.Position.X, worker.Position.Z)) >
            Vector2.DistanceSquared(new(last.X, last.Z), new(worker.Position.X, worker.Position.Z))) cells.Reverse();
        Cancel(id);
        worker.BeginEarthwork(preview.Order);
        _jobs[id] = new Job(worker, request.SenderId, preview.Order, cells);
        return new(NetworkMessageType.EarthworkStartCommand, hostId, UnitId: id, EarthworkOrder: preview.Order);
    }

    public void CancelForRequest(NetworkMessage request)
    {
        if (request.Type is not (NetworkMessageType.GotoRequest or NetworkMessageType.StopRequest or
            NetworkMessageType.FollowRequest or NetworkMessageType.AttackTargetRequest or NetworkMessageType.AttackGroundRequest or
            NetworkMessageType.BuildConstructionRequest or NetworkMessageType.EnterUnitRequest)) return;
        foreach (Guid id in request.UnitIds ?? (request.UnitId is Guid single ? new[] { single } : Array.Empty<Guid>()))
            if (_jobs.TryGetValue(id, out Job? job) && Globals.Game.Armies.CanControl(request.SenderId, job.Worker.ArmyId)) Cancel(id);
    }

    public void Cancel(Guid id)
    {
        if (!_jobs.Remove(id, out Job? job)) return;
        job.Worker.EndEarthwork();
        publish(new(NetworkMessageType.EarthworkEndCommand, hostId, UnitId: id, EarthworkOrderId: job.Order.Id));
    }

    public void Update(float seconds)
    {
        foreach (Job job in _jobs.Values.ToArray())
        {
            GDIBulldozer worker = job.Worker;
            if (world.Units.FindById(worker.UnitId) != worker || worker.IsDying || worker.IsEmbarked ||
                !Globals.Game.Armies.CanControl(job.Player, worker.ArmyId) || worker.Occupancy?.IsOperational == false)
            { Cancel(worker.UnitId); continue; }
            while (job.Index < job.Cells.Count && !Earthwork.NeedsWork(world, job.Order, job.Cells[job.Index])) job.Index++;
            if (job.Index == job.Cells.Count) { Cancel(worker.UnitId); continue; }
            Point cell = job.Cells[job.Index];
            if (!Earthwork.CanWork(world, worker, job.Order, cell))
            { job.WorkTime = 0; continue; }
            Vector3 target = world.GameGrid.ToWorldPosition(cell, 0);
            float reach = (Math.Max(worker.Width, worker.Length) * 0.5f + 0.75f) * world.GameGrid.CellSize;
            bool inReach = Vector2.Distance(new(worker.Position.X, worker.Position.Z), new(target.X, target.Z)) <= reach;
            if (job.Approach is Point approach && world.GameGrid.ToCell(worker.Position) == approach &&
                (worker.CurrentCommand is null || worker.PlannedPath.Count == 0) && inReach)
            {
                job.WorkTime += seconds;
                if (job.WorkTime < Earthwork.Duration(world, job.Order, cell)) continue;
                worker.ApplyEarthworkCell(world, job.Order.Id, ++job.Sequence, cell);
                publish(new(NetworkMessageType.EarthworkCellCommand, hostId, UnitId: worker.UnitId,
                    EarthworkOrderId: job.Order.Id, EarthworkSequence: job.Sequence, CellX: cell.X, CellZ: cell.Y));
                job.Index++; job.WorkTime = 0; job.Approach = null;
                continue;
            }
            job.RetryTime -= seconds;
            if (Vector3.DistanceSquared(worker.Position, job.LastPosition) > 0.01f) job.StalledTime = 0;
            else job.StalledTime += seconds;
            job.LastPosition = worker.Position;
            if (job.Approach is not null && worker.CurrentCommand is not null && job.StalledTime < 5) continue;
            if (job.RetryTime > 0) continue;
            job.RetryTime = 2; job.WorkTime = 0; job.StalledTime = 0;
            Point? destination = FindApproach(worker, cell, reach);
            if (destination is not Point next) continue; // Wait; occupied routes may open again.
            job.Approach = next;
            Vector3 nextPosition = world.GameGrid.ToWorldPosition(next, 0);
            if (world.GameGrid.ToCell(worker.Position) == next) continue;
            var command = new NetworkMessage(NetworkMessageType.GotoCommand, hostId, UnitIds: new[] { worker.UnitId },
                X: nextPosition.X, Z: nextPosition.Z, EarthworkOrderId: job.Order.Id);
            worker.TryReceiveGotoCommand(world, new GotoCommand(new(nextPosition.X, nextPosition.Z)));
            publish(command);
        }
    }

    private Point? FindApproach(GDIBulldozer worker, Point cell, float reach)
    {
        Vector3 target = world.GameGrid.ToWorldPosition(cell, 0);
        List<Point> candidates = [];
        int radius = (int)MathF.Ceiling(reach / world.GameGrid.CellSize);
        for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                Point p = cell + new Point(x, z);
                if (new Vector2(x, z).Length() * world.GameGrid.CellSize <= reach &&
                    worker.MovementProfile.CanEnter(world, worker, p) && world.GameGrid.IsPathfindingAllowed(worker, p)) candidates.Add(p);
            }
        // Prefer driving across the actual work tile, with reachable blade positions as fallback.
        foreach (Point p in candidates.OrderBy(p => Vector2.DistanceSquared(p.ToVector2(), cell.ToVector2())))
        {
            Vector3 point = world.GameGrid.ToWorldPosition(p, 0);
            if (new Pathfinder(world).TryFindPath_AStar(worker, worker.MovementProfile, new(point.X, point.Z), out _)) return p;
        }
        return null;
    }
}
