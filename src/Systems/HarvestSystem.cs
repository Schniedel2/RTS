using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

/// <summary>Schedules movement through the host planner. Completion/cancellation run on the game thread.</summary>
public delegate void HarvestMovementPlanner(Harvester unit,
    IEnumerable<(Point? Resource, Vector3 Position)> candidates, Func<bool> valid,
    Action<Point?, NetworkMessage?> completed, Action cancelled);

/// <summary>Host-only harvest jobs. Clients apply replicated state; all work stays on the game thread.</summary>
public sealed class HarvestSystem
{
    private readonly GameWorld _world;
    private readonly ArmyHandler _armies;
    private readonly Guid _peerId;
    private readonly Func<NetworkMessage, Task> _publish;
    private readonly HarvestMovementPlanner _planMovement;
    private readonly Func<long> _generation;
    private readonly Func<Guid, long> _version;
    private readonly Dictionary<Guid, HarvestJob> _jobs = [];
    private double _hostTime;
    private const int HarvestSearchRadius = 24;
    private const float HarvestRatePerSecond = 18.0f;
    private const float UnloadSeconds = 2.0f;
    public HarvestSystem(GameWorld world, ArmyHandler armies, Guid peerId,
        Func<NetworkMessage, Task> publish, HarvestMovementPlanner planMovement,
        Func<long> generation, Func<Guid, long> version)
    {
        _world = world;
        _armies = armies;
        _peerId = peerId;
        _publish = publish;
        _planMovement = planMovement;
        _generation = generation;
        _version = version;
    }
    public int ActiveJobCount => _jobs.Count;
    // Cancels host job ownership. The caller publishes the corresponding Stop
    // or replacing command; detached planner callbacks cannot revive this job.
    public void Cancel(Guid unitId) => _jobs.Remove(unitId);
    public void Reset() => _jobs.Clear();
    public void CancelForRequest(NetworkMessage request)
    {
        if (request.Type is not (NetworkMessageType.GotoRequest or NetworkMessageType.MoveAwayRequest)) return;
        IEnumerable<Guid> ids = request.UnitIds ?? [];
        if (request.UnitId is Guid single) ids = ids.Append(single);
        foreach (Guid id in ids.Distinct())
            if (_world.Units.FindById(id) is Unit unit && _armies.CanControl(request.SenderId, unit.ArmyId))
                Cancel(id);
    }
    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b) =>
        Vector2.DistanceSquared(new(a.X, a.Z), new(b.X, b.Z));

    private sealed class HarvestJob(Point fieldCenter)
    {
        public Point FieldCenter { get; } = fieldCenter;
        public HarvestPhase Phase { get; set; } = HarvestPhase.DrivingToField;
        public Point? ResourceCell { get; set; }
        public Guid? SiloId { get; set; }
        public bool MoveIssued { get; set; }
        public float UnloadElapsed { get; set; }
        public bool ResumeAfterUnload { get; set; } = true;
        public Vector3? StorageApproach { get; set; }
        public HashSet<Point> FailedStorageApproaches { get; } = [];
        public double RetryAfter { get; set; }
        public bool Planning { get; set; }
    }
    public NetworkMessage? TryStart(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindById(id) is not Harvester harvester ||
            harvester.IsDying || !_armies.CanControl(request.SenderId, harvester.ArmyId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Z)) return null;
        Point center = _world.GameGrid.ToCell(new Vector3(request.X, 0, request.Z));
        if (!_world.GameGrid.Contains(center)) return null;
        harvester.Stop();
        harvester.ApplyHarvestState(HarvestPhase.DrivingToField, harvester.CargoAmount);
        _jobs[id] = new HarvestJob(center);
        return CreateHarvestStateCommand(harvester, HarvestPhase.DrivingToField);
    }

    public NetworkMessage? TryReturn(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindById(id) is not Harvester harvester ||
            harvester.IsDying || harvester.CargoAmount <= 0.001f ||
            !_armies.CanControl(request.SenderId, harvester.ArmyId) ||
            FindNearestSilo(harvester) is not Building storage)
            return null;

        harvester.Stop();
        harvester.ApplyHarvestState(HarvestPhase.ReturningToSilo, harvester.CargoAmount);
        _jobs[id] = new HarvestJob(_world.GameGrid.ToCell(harvester.Position))
        {
            Phase = HarvestPhase.ReturningToSilo,
            SiloId = storage.UnitId,
            ResumeAfterUnload = false
        };
        return CreateHarvestStateCommand(harvester, HarvestPhase.ReturningToSilo);
    }

    public async Task UpdateAsync(GameTime gameTime, double hostTime)
    {
        _hostTime = hostTime;
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        foreach ((Guid id, HarvestJob job) in _jobs.ToArray())
        {
            if (_world.Units.FindById(id) is not Harvester harvester || harvester.IsDying)
            {
                _jobs.Remove(id);
                continue;
            }
            if (_hostTime < job.RetryAfter)
                continue;
            if (job.Planning) continue;
            if (job.Phase == HarvestPhase.DrivingToField)
            {
                if (harvester.CargoAmount >= harvester.CargoCapacity - 0.001f)
                {
                    await BeginReturnAsync(harvester, job); continue;
                }
                if (job.ResourceCell is not Point resourceCell ||
                    !_world.Tiberium.Cells.TryGetValue(resourceCell, out TiberiumCell? resource) ||
                    resource.Amount <= 0.001f)
                {
                    if (!TryFindTiberium(job.FieldCenter, out resourceCell))
                    {
                        if (harvester.CargoAmount <= 0.001f) DelayHarvestRetry(job);
                        else await BeginReturnAsync(harvester, job);
                        continue;
                    }
                    job.ResourceCell = resourceCell;
                    job.MoveIssued = false;
                }
                Vector3 target = _world.GameGrid.ToWorldPosition(resourceCell, 0);
                if (harvester.IsWithinHarvestReach(target, _world.GameGrid.CellSize))
                {
                    job.Phase = HarvestPhase.Harvesting;
                    job.MoveIssued = false;
                    await PublishHarvestStateAsync(harvester, job.Phase);
                    continue;
                }
                if (!job.MoveIssued)
                {
                    if (await TryPublishReachableHarvestGotoAsync(harvester, job, resourceCell))
                        job.MoveIssued = true;
                    else
                    {
                        // Do not retry the same inaccessible crystal forever.
                        // The next pass chooses another resource cell.
                        job.ResourceCell = null;
                        DelayHarvestRetry(job);
                    }
                    continue;
                }
                if (harvester.CurrentCommand is null)
                {
                    if (harvester.IsWithinHarvestReach(target, _world.GameGrid.CellSize))
                    {
                        job.Phase = HarvestPhase.Harvesting; job.MoveIssued = false;
                        await PublishHarvestStateAsync(harvester, job.Phase);
                    }
                    else job.MoveIssued = false;
                }
                continue;
            }
            if (job.Phase == HarvestPhase.Harvesting)
            {
                if (job.ResourceCell is not Point cell || !_world.Tiberium.Cells.ContainsKey(cell))
                {
                    job.Phase = HarvestPhase.DrivingToField;
                    await PublishHarvestStateAsync(harvester, job.Phase); continue;
                }
                float harvested = _world.Tiberium.TryHarvest(cell,
                    Math.Min(harvester.CargoCapacity - harvester.CargoAmount, HarvestRatePerSecond * elapsed), _hostTime);
                if (harvested > 0)
                {
                    harvester.ApplyHarvestState(job.Phase, harvester.CargoAmount + harvested);
                    float remaining = _world.Tiberium.Cells.TryGetValue(cell, out TiberiumCell? value) ? value.Amount : 0;
                    await _publish(new(NetworkMessageType.TiberiumHarvestCommand, _peerId,
                        CellX: cell.X, CellZ: cell.Y, TiberiumAmount: remaining, ServerTime: _hostTime));
                    await PublishHarvestStateAsync(harvester, job.Phase);
                }
                if (harvester.CargoAmount >= harvester.CargoCapacity - 0.001f) await BeginReturnAsync(harvester, job);
                else if (!_world.Tiberium.Cells.ContainsKey(cell))
                {
                    job.Phase = HarvestPhase.DrivingToField;
                    await PublishHarvestStateAsync(harvester, job.Phase);
                }
                continue;
            }
            if (job.Phase == HarvestPhase.ReturningToSilo)
            {
                Building? silo = job.SiloId is Guid siloId ? _world.Units.FindById(siloId) as Building : null;
                if (silo is null || silo.IsDying || !silo.IsCompleted || silo.ArmyId != harvester.ArmyId ||
                    silo.AvailableResourceCapacity <= 0.001f)
                {
                    silo = FindNearestSilo(harvester); job.SiloId = silo?.UnitId;
                    job.MoveIssued = false; job.StorageApproach = null;
                    job.FailedStorageApproaches.Clear();
                }
                if (silo is null)
                {
                    DelayHarvestRetry(job);
                    continue;
                }
                if (job.StorageApproach is not Vector3 unload)
                {
                    Vector3 preferredUnload = silo.GetResourceUnloadPosition();
                    if (!ProductionExitResolver.TryResolve(_world, silo, harvester,
                        harvester.Position, preferredUnload, out unload,
                        cell => !job.FailedStorageApproaches.Contains(cell)))
                    {
                        // Reconsider transient obstructions after the retry delay.
                        job.FailedStorageApproaches.Clear();
                        DelayHarvestRetry(job);
                        continue;
                    }
                    job.StorageApproach = unload;
                }
                if (HorizontalDistanceSquared(harvester.Position, unload) <= 6.25f)
                {
                    job.Phase = HarvestPhase.Unloading;
                    job.UnloadElapsed = 0.0f;
                    job.MoveIssued = false;
                    await PublishHarvestStateAsync(harvester, job.Phase);
                    continue;
                }
                if (job.MoveIssued && harvester.MovementRetryCount >= 3)
                {
                    job.FailedStorageApproaches.Add(_world.GameGrid.ToCell(unload));
                    job.StorageApproach = null;
                    job.MoveIssued = false;
                    // Stop only the confirmed approach; keep the host harvest job.
                    await _publish(new(NetworkMessageType.StopCommand, _peerId,
                        UnitIds: [harvester.UnitId]));
                    await PublishHarvestStateAsync(harvester, job.Phase);
                    DelayHarvestRetry(job);
                    continue;
                }
                if (!job.MoveIssued)
                {
                    job.MoveIssued = await PublishHarvesterGotoAsync(harvester, unload);
                    if (!job.MoveIssued) DelayHarvestRetry(job);
                    continue;
                }
                if (harvester.CurrentCommand is null)
                {
                    if (HorizontalDistanceSquared(harvester.Position, unload) <= 6.25f)
                    {
                        job.Phase = HarvestPhase.Unloading; job.UnloadElapsed = 0;
                        await PublishHarvestStateAsync(harvester, job.Phase);
                    }
                    else job.MoveIssued = false;
                }
                continue;
            }
            job.UnloadElapsed += elapsed;
            if (job.UnloadElapsed < UnloadSeconds) continue;
            Building? storage = job.SiloId is Guid storageId ? _world.Units.FindById(storageId) as Building : null;
            if (storage is null || storage.IsDying || storage.ArmyId != harvester.ArmyId || !storage.IsCompleted)
            {
                await BeginReturnAsync(harvester, job);
                continue;
            }
            float accepted = storage.StoreResources(harvester.CargoAmount);
            harvester.ApplyHarvestState(HarvestPhase.Unloading, harvester.CargoAmount - accepted);
            if (harvester.ArmyId is Guid armyId && _armies.Find(armyId) is Army army)
            {
                army.Resources += (int)MathF.Floor(accepted);
                await _publish(new(NetworkMessageType.ArmyResourcesCommand, _peerId,
                    ArmyId: armyId, ResourceAmount: army.Resources));
            }
            if (!job.ResumeAfterUnload && harvester.CargoAmount <= 0.001f)
            {
                await EndHarvestAsync(harvester);
                continue;
            }
            job.Phase = harvester.CargoAmount > 0.001f ? HarvestPhase.ReturningToSilo : HarvestPhase.DrivingToField;
            harvester.ApplyHarvestState(job.Phase, harvester.CargoAmount);
            job.SiloId = null; job.MoveIssued = false; job.StorageApproach = null;
            await PublishHarvestStateAsync(harvester, job.Phase);
        }
    }

    private bool TryFindTiberium(Point center, out Point cell)
    {
        Point? nearest = _world.Tiberium.Cells.Where(pair => pair.Value.Amount > 0.001f &&
            Math.Abs(pair.Key.X - center.X) <= HarvestSearchRadius && Math.Abs(pair.Key.Y - center.Y) <= HarvestSearchRadius)
            .OrderBy(pair => Math.Abs(pair.Key.X - center.X) + Math.Abs(pair.Key.Y - center.Y))
            .Select(pair => (Point?)pair.Key).FirstOrDefault();
        cell = nearest ?? default;
        return nearest.HasValue;
    }

    private Task<bool> TryPublishReachableHarvestGotoAsync(Harvester harvester, HarvestJob job, Point preferredResource)
    {
        Point[] resources = _world.Tiberium.Cells
            .Where(pair => pair.Value.Amount > 0.001f &&
                Math.Abs(pair.Key.X - job.FieldCenter.X) <= HarvestSearchRadius &&
                Math.Abs(pair.Key.Y - job.FieldCenter.Y) <= HarvestSearchRadius)
            .OrderBy(pair => pair.Key == preferredResource ? 0 : 1)
            .ThenBy(pair => Math.Abs(pair.Key.X - job.FieldCenter.X) + Math.Abs(pair.Key.Y - job.FieldCenter.Y))
            .Select(pair => pair.Key).Take(16).ToArray();
        IEnumerable<(Point? Resource, Vector3 Position)> Candidates()
        {
            foreach (Point resource in resources)
                foreach (Point approach in HarvestApproachCells(resource))
                {
                    Vector3 target = _world.GameGrid.ToWorldPosition(approach, harvester.Position.Y);
                    if (HorizontalDistanceSquared(target, _world.GameGrid.ToWorldPosition(resource, target.Y)) <= 4.0f)
                        yield return (resource, target);
                }
        }
        return QueueHarvestMovement(harvester, job, Candidates());
    }

    private Task<bool> QueueHarvestMovement(Harvester harvester, HarvestJob job,
        IEnumerable<(Point? Resource, Vector3 Position)> candidates)
    {
        if (job.Planning) return Task.FromResult(true);
        long version = _version(harvester.UnitId);
        int pathRequest = harvester._pathRequestId;
        HarvestPhase phase = job.Phase;
        Guid? silo = job.SiloId;
        long generation = _generation();
        bool Valid() => generation == _generation() &&
            _jobs.TryGetValue(harvester.UnitId, out HarvestJob? current) && ReferenceEquals(current, job) &&
            job.Phase == phase && !harvester.IsDying && !harvester.IsEmbarked &&
            ReferenceEquals(_world.Units.FindById(harvester.UnitId), harvester) &&
            version == _version(harvester.UnitId) && pathRequest == harvester._pathRequestId &&
            (phase != HarvestPhase.ReturningToSilo || silo == job.SiloId &&
                silo is Guid storageId && _world.Units.FindById(storageId) is Building storage && !storage.IsDying && storage.IsCompleted &&
                storage.ArmyId == harvester.ArmyId && storage.AvailableResourceCapacity > 0.001f);
        IEnumerable<(Point? Resource, Vector3 Position)> LiveCandidates()
        {
            foreach (var candidate in candidates)
            {
                if (!Valid()) yield break;
                if (candidate.Resource is Point crystal &&
                    (!_world.Tiberium.Cells.TryGetValue(crystal, out TiberiumCell? resource) || resource.Amount <= 0.001f)) continue;
                yield return candidate;
            }
        }
        job.Planning = true;
        _planMovement(harvester, LiveCandidates(), Valid, (chosenResource, result) =>
        {
            if (!Valid()) return;
            job.Planning = false;
            if (result is null)
            {
                if (phase == HarvestPhase.DrivingToField) job.ResourceCell = null;
                if (phase == HarvestPhase.ReturningToSilo && job.StorageApproach is Vector3 failed)
                {
                    job.FailedStorageApproaches.Add(_world.GameGrid.ToCell(failed));
                    job.StorageApproach = null;
                }
                DelayHarvestRetry(job);
                return;
            }
            if (chosenResource is Point resource) job.ResourceCell = resource;
            _publish(result).GetAwaiter().GetResult();
            job.MoveIssued = true;
        }, () => { job.Planning = false; job.MoveIssued = false; });
        return Task.FromResult(true); // Accepted for planning, not yet a movement command.
    }
    private static IEnumerable<Point> HarvestApproachCells(Point resource)
    {
        yield return resource;
        for (int radius = 1; radius <= 2; radius++)
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == radius)
                        yield return resource + new Point(x, y);
    }

    private Building? FindNearestSilo(Harvester harvester) => _world.Units.Units.OfType<Building>()
        .Where(silo => silo.ResourceCapacity > 0.0f && silo.AvailableResourceCapacity > 0.001f &&
            silo.ArmyId == harvester.ArmyId && silo.IsCompleted && !silo.IsDying)
        .OrderBy(silo => HorizontalDistanceSquared(harvester.Position, silo.Position)).FirstOrDefault();

    private async Task BeginReturnAsync(Harvester harvester, HarvestJob job)
    {
        job.Phase = HarvestPhase.ReturningToSilo; job.MoveIssued = false;
        job.SiloId = FindNearestSilo(harvester)?.UnitId;
        job.StorageApproach = null;
        job.FailedStorageApproaches.Clear();
        await PublishHarvestStateAsync(harvester, job.Phase);
    }

    private void DelayHarvestRetry(HarvestJob job)
    {
        job.MoveIssued = false;
        job.RetryAfter = _hostTime + Harvester.HarvestRetrySeconds;
    }

    private async Task EndHarvestAsync(Harvester harvester)
    {
        _jobs.Remove(harvester.UnitId);
        harvester.ApplyHarvestState(HarvestPhase.Idle, harvester.CargoAmount);
        await PublishHarvestStateAsync(harvester, HarvestPhase.Idle);
    }

    private Task<bool> PublishHarvesterGotoAsync(Harvester harvester, Vector3 target) =>
        _jobs.TryGetValue(harvester.UnitId, out HarvestJob? job)
            ? QueueHarvestMovement(harvester, job, new[] { ((Point?)null, target) })
            : Task.FromResult(false);
    private NetworkMessage CreateHarvestStateCommand(Harvester harvester, HarvestPhase phase) =>
        new(NetworkMessageType.HarvestCommand, _peerId, UnitId: harvester.UnitId,
            HarvestPhase: phase, CargoAmount: harvester.CargoAmount);

    private Task PublishHarvestStateAsync(Harvester harvester, HarvestPhase phase) => _publish(CreateHarvestStateCommand(harvester, phase));

}
