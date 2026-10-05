using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
namespace RTS;
public sealed class ScoutingController(GameWorld world, Guid? commandPlayerId = null,
    Network.NetworkHandler? commandNetwork = null) : IDisposable
{
    private sealed class State
    {
        public Point Target;
        public bool HasTarget, Planning;
        public float ReconsiderIn;
        public int Version;
        public readonly Dictionary<Point, double> FailedSectors = [];
        public Network.LocalRequestReceipt? Receipt;
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
    public void Stop(IEnumerable<Unit> units) { foreach (Unit unit in units) { _scouts.Remove(unit.UnitId); world.ScoutingTargets.Release(unit.UnitId, _controller); } }
    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.Scouting");
        double now = gameTime.TotalGameTime.TotalSeconds;
        if (now < _now)
            foreach (State previous in _scouts.Values) { previous.FailedSectors.Clear(); previous.HasTarget = false; previous.Planning = false; previous.Version++; }
        _now = now;
        world.ScoutingTargets.Clean(now);
        foreach ((Guid id, State state) in _scouts.ToArray())
        {
            if (world.Units.FindById(id) is not MobileUnit unit || unit.IsDying || unit.IsEmbarked || unit.ArmyId is not Guid army) { _scouts.Remove(id); world.ScoutingTargets.Release(id, _controller); continue; }
            state.ReconsiderIn -= (float)gameTime.ElapsedGameTime.TotalSeconds;
            Point current = world.GameGrid.ToCell(unit.Position);
            foreach (Point sector in state.FailedSectors.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                state.FailedSectors.Remove(sector);
            if (state.Planning) { world.ScoutingTargets.Claim(id, army, state.Target, _controller, now); continue; }
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
                world.Visibility.GetDisplayedTerrainVisibility(army, state.Target, false) == VisibilityState.Unexplored)
            { world.ScoutingTargets.Claim(id, army, state.Target, _controller, now); continue; }
            world.ScoutingTargets.Release(id, _controller);
            if (!TryFindTarget(unit, army, current, state, out Point target) ||
                !world.ScoutingTargets.Claim(id, army, target, _controller, now))
            { state.HasTarget = false; state.ReconsiderIn = 2; continue; }
            state.HasTarget = false; state.Target = target; state.Planning = true;
            int version = ++state.Version;
            long generation = (world.SimulationNetwork ?? Globals.Game?.Network)?.SessionGeneration ?? -1;
            bool Valid() => !_disposed && generation == ((world.SimulationNetwork ?? Globals.Game?.Network)?.SessionGeneration ?? -1) && _scouts.TryGetValue(id, out State? active) && ReferenceEquals(active, state) &&
                state.Version == version && state.Planning && state.Target == target && world.Units.FindById(id) == unit && !unit.IsDying &&
                !unit.IsEmbarked && unit.ArmyId == army && world.GameGrid.ToCell(unit.Position) == current;
            void Cancelled() { if (state.Version != version || !_scouts.TryGetValue(id, out State? active) || !ReferenceEquals(active, state)) return; state.Planning = false; state.ReconsiderIn = 0; world.ScoutingTargets.Release(id, _controller); }
            void Complete(bool reachable)
            {
                state.Planning = false;
                if (!reachable)
                {
                    state.FailedSectors[ScoutingTargets.Sector(target)] = _now + 30;
                    state.ReconsiderIn = 0;
                    world.ScoutingTargets.Release(id, _controller);
                    return;
                }
                if (!world.ScoutingTargets.Claim(id, army, target, _controller, _now)) { state.ReconsiderIn = 2; return; }
                state.HasTarget = true; state.ReconsiderIn = 8;
                state.Receipt = null;
                Vector3 position = world.GameGrid.ToWorldPosition(target, 0);
                if (commandPlayerId is Guid playerId)
                {
                    var commands = new Network.PlayerCommandService(commandNetwork ?? Globals.Game.Network, playerId);
                    _ = commands.GotoAsync([unit.UnitId], position);
                    state.Receipt = commands.LastRequest;
                }
                else _ = Globals.Game.NetworkClient.RequestGotoAsync([unit.UnitId], position.X, position.Y, position.Z);
            }
            // Helicopter Goto uses flight navigation, independent of ground occupancy and slopes.
            if (unit is Helicopter) Complete(true);
            else
            {
                Vector3 destination = world.GameGrid.ToWorldPosition(target, 0);
                Pathfinder.Search search = world.PathfindingManager.CreateSearch(unit, current, new(destination.X, destination.Z));
                (world.IsMovementAuthority ? world.PathfindingManager.Scheduler : world.ScoutingTargets.ClientPlanning).Enqueue(search.Work(), Valid, () => Complete(search.Succeeded), Cancelled,
                    "AI.ScoutReachability");
            }
        }
        world.ScoutingTargets.UpdateClientPlanning(now);
    }
    private bool TryFindTarget(MobileUnit unit, Guid army, Point current, State state, out Point target)
    {
        List<Point> candidates = []; int min = Math.Max(4, unit.GetSightRange() / 2), max = Math.Max(12, unit.GetSightRange() * 3);
        for (int z = Math.Max(0, current.Y - max); z <= Math.Min(world.GameGrid.Height - 1, current.Y + max); z++)
        for (int x = Math.Max(0, current.X - max); x <= Math.Min(world.GameGrid.Width - 1, current.X + max); x++)
        { Point cell = new(x, z); int d = Math.Max(Math.Abs(x-current.X), Math.Abs(z-current.Y));
          if (d >= min && d <= max && world.Visibility.GetDisplayedTerrainVisibility(army, cell, false) == VisibilityState.Unexplored && !state.FailedSectors.ContainsKey(ScoutingTargets.Sector(cell)) &&
              (unit is Helicopter || unit.MovementProfile.CanUseTerrain(world.GameGrid.GetCell(cell)))) candidates.Add(cell); }
        if (candidates.Count == 0) { target = default; return false; }
        var groups = candidates.GroupBy(ScoutingTargets.Sector)
            .Where(group => !world.ScoutingTargets.Reserved(army, group.Key, unit.UnitId))
            .Select(group => new { Cells = group.ToArray(), Score = ScoreSector(group.Key, group.Count(), army, current, unit.UnitId) })
            .OrderByDescending(group => group.Score).ToArray();
        if (groups.Length == 0) { target = default; return false; }
        Point[] cells = groups[0].Cells;
        target = cells[Random.Shared.Next(cells.Length)]; return true;
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
        _disposed = true;
        world.ScoutingTargets.ReleaseController(_controller);
        _scouts.Clear();
    }

}
