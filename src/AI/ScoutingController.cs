using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
namespace RTS;
public sealed class ScoutingController(GameWorld world, Guid? commandPlayerId = null,
    Network.NetworkHandler? commandNetwork = null, float reconsiderSeconds = 8) : IDisposable
{
    private sealed class State
    {
        public Point Target;
        public bool HasTarget, Planning, Selecting;
        public readonly ScoutingCandidateSearch Candidates = new();
        public float ReconsiderIn;
        public int Version;
        public readonly Dictionary<Point, double> FailedSectors = [];
        public Network.RequestReceipt? Receipt;
        public Vector3 LastPosition;
        public float StalledFor;
    }
    private double _now;
    private bool _disposed;
    private readonly Dictionary<Guid, State> _scouts = [];
    private readonly Guid _controller = Guid.NewGuid();
    public int ActiveScoutCount => _scouts.Count;
    public bool IsScouting(Guid unitId) => _scouts.ContainsKey(unitId);
    public void Start(IEnumerable<Unit> units) { foreach (MobileUnit unit in units.OfType<MobileUnit>()) if (!unit.IsDying && !unit.IsEmbarked) _scouts.TryAdd(unit.UnitId, new() { LastPosition = unit.Position }); }
    public void Stop(IEnumerable<Unit> units) { foreach (Unit unit in units) { _scouts.Remove(unit.UnitId); if (commandPlayerId is not null && unit.ArmyId is Guid army) world.UnitTasks.Release(unit.UnitId, $"{army}:scout:{_controller}"); world.ScoutingTargets.Release(unit.UnitId, _controller); } }
    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.Scouting");
        double now = gameTime.TotalGameTime.TotalSeconds;
        if (now < _now)
            foreach (State previous in _scouts.Values) { previous.FailedSectors.Clear(); previous.HasTarget = false; previous.Planning = false; previous.Version++; }
        _now = now;
        world.ScoutingTargets.Clean(now);
        if (commandPlayerId is not null) world.UnitTasks.Update(now);
        foreach ((Guid id, State state) in _scouts.ToArray())
        {
            if (world.Units.FindById(id) is not MobileUnit unit || unit.IsDying || unit.IsEmbarked || unit.ArmyId is not Guid army) { _scouts.Remove(id); world.ScoutingTargets.Release(id, _controller); continue; }
            AIUnitTaskAgent? task = commandPlayerId is null ? null : new(world, $"{army}:scout:{_controller}", AIUnitTask.Scout, 30);
            if (task is not null && !task.CanUse(unit)) continue;
            task?.Authorize([id]);
            state.ReconsiderIn -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            Point current = world.GameGrid.ToCell(unit.Position);
            foreach (Point sector in state.FailedSectors.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                state.FailedSectors.Remove(sector);
            if (state.Planning) { if (!state.Selecting) world.ScoutingTargets.Claim(id, army, state.Target, _controller, now); continue; }
            if (state.HasTarget)
            {
                if (Vector3.DistanceSquared(unit.Position, state.LastPosition) > 0.01f) { state.LastPosition = unit.Position; state.StalledFor = 0; }
                else state.StalledFor += (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (state.Receipt?.State is Network.LocalRequestState.Rejected or Network.LocalRequestState.Abandoned || state.StalledFor >= 15)
                { state.FailedSectors[ScoutingTargets.Sector(state.Target)] = now + 30; state.HasTarget = false; state.StalledFor = 0; state.ReconsiderIn = 0; }
            }
            if (!state.HasTarget && state.ReconsiderIn > 0) continue;
            if (state.ReconsiderIn > 0 && current != state.Target &&
                world.ScoutingTargets.TargetOf(id) == state.Target &&
                world.Visibility.GetSimulationVisibility(army, state.Target) == VisibilityState.Unexplored)
            { world.ScoutingTargets.Claim(id, army, state.Target, _controller, now); continue; }
            world.ScoutingTargets.Release(id, _controller);
            state.HasTarget = false; state.Planning = true; state.Selecting = true;
            Point target = default;
            bool found = false, reachable = false;
            int version = ++state.Version;
            long generation = world.SimulationNetwork?.SessionGeneration ?? -1;
            bool Valid() => !_disposed && generation == (world.SimulationNetwork?.SessionGeneration ?? -1) && _scouts.TryGetValue(id, out State? active) && ReferenceEquals(active, state) &&
                (task is null || task.CanUse(unit)) && state.Version == version && state.Planning && world.Units.FindById(id) == unit && !unit.IsDying &&
                !unit.IsEmbarked && unit.ArmyId == army && world.GameGrid.ToCell(unit.Position) == current;
            void Cancelled() { if (state.Version != version || !_scouts.TryGetValue(id, out State? active) || !ReferenceEquals(active, state)) return; state.Planning = false; state.ReconsiderIn = 0; world.ScoutingTargets.Release(id, _controller); }
            void Complete()
            {
                state.Planning = false; state.Selecting = false;
                if (!found) { state.ReconsiderIn = 2; return; }
                if (!reachable)
                {
                    state.FailedSectors[ScoutingTargets.Sector(target)] = _now + 30;
                    state.ReconsiderIn = 0;
                    world.ScoutingTargets.Release(id, _controller);
                    return;
                }
                if (!world.ScoutingTargets.Claim(id, army, target, _controller, _now)) { state.ReconsiderIn = 2; return; }
                state.HasTarget = true; state.ReconsiderIn = reconsiderSeconds;
                state.Receipt = null;
                Vector3 position = world.GameGrid.ToWorldPosition(target, 0);
                if (commandPlayerId is Guid playerId)
                {
                    var commands = new Network.PlayerCommandService(commandNetwork ?? world.SimulationNetwork ?? throw new InvalidOperationException("Scouting requires a configured simulation network."), playerId, task);
                    _ = commands.GotoAsync([unit.UnitId], position);
                    state.Receipt = commands.LastRequest;
                }
                else
                {
                    Network.NetworkHandler network = world.SimulationNetwork ?? throw new InvalidOperationException("Scouting requires a configured simulation network.");
                    var commands = new Network.PlayerCommandService(network, network.LocalPeerId);
                    _ = commands.GotoAsync([unit.UnitId], position);
                    state.Receipt = commands.LastRequest;
                }
            }
            IEnumerable<int> Work()
            {
                foreach (int step in state.Candidates.Work(world, unit, army, current,
                    sector => state.FailedSectors.ContainsKey(sector),
                    (sector, count) => ScoreSector(sector, count, army, current, id),
                    reserve: cell => world.ScoutingTargets.Claim(id, army, cell, _controller, _now))) yield return step;
                if (state.Candidates.Target is not Point candidate) yield break;
                target = candidate; state.Target = target; state.Selecting = false; found = true;
                // Expose the lease before the reachability search starts on the next slice.
                yield return 0;
                if (unit is Helicopter) { reachable = true; yield break; }
                Vector3 destination = world.GameGrid.ToWorldPosition(target, 0);
                Pathfinder.Search search = world.PathfindingManager.CreateSearch(unit, current, new(destination.X, destination.Z));
                foreach (int step in search.Work()) yield return step;
                reachable = search.Succeeded;
            }
            (world.IsMovementAuthority ? world.PathfindingManager.Scheduler : world.ScoutingTargets.ClientPlanning)
                .Enqueue(Work(), Valid, Complete, Cancelled, "AI.ScoutSearch");
        }
        world.ScoutingTargets.UpdateClientPlanning(now);
    }
    private float ScoreSector(Point sector, int unknownCells, Guid army, Point current, Guid scoutId)
    {
        Vector2 direction = new(sector.X * ScoutingTargets.SectorSize + 8 - current.X,
            sector.Y * ScoutingTargets.SectorSize + 8 - current.Y);
        float distance = direction.Length();
        if (distance > 0) direction /= distance;
        float overlap = 0;
        foreach (Point other in world.ScoutingTargets.OtherTargets(army, scoutId))
        {
            Vector2 delta = new(other.X - current.X, other.Y - current.Y);
            if (delta.LengthSquared() > 0) overlap = Math.Max(overlap, Math.Max(0, Vector2.Dot(direction, Vector2.Normalize(delta))));
        }
        return unknownCells / 256f * 20 - distance * 0.05f - overlap * overlap * 40;
    }

    public void Dispose()
    {
        foreach (Guid id in _scouts.Keys)
            if (commandPlayerId is not null && world.Units.FindById(id)?.ArmyId is Guid army)
                world.UnitTasks.Release(id, $"{army}:scout:{_controller}");
        _disposed = true;
        world.ScoutingTargets.ReleaseController(_controller);
        _scouts.Clear();
    }

}
