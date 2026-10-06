using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

/// <summary>Local, shared army reservations for automatic scouting, including human scouts.</summary>
public sealed class ScoutingTargets(GameWorld world)
{
    public const int SectorSize = 16;
    private sealed record Lease(Guid Army, Point Cell, Guid Controller, double Until);
    private readonly Dictionary<Guid, Lease> _leases = [];
    internal PlanningScheduler ClientPlanning { get; } = new();
    private double _lastClientPlanningTime = double.NaN;
    internal void UpdateClientPlanning(double now)
    {
        if (world.IsMovementAuthority || now == _lastClientPlanningTime) return;
        _lastClientPlanningTime = now;
        ClientPlanning.Update(Math.Min(512, AIRuntimeSettings.LocalCompute.PlanningStepsPerUpdate),
            Math.Min(0.5, AIRuntimeSettings.LocalCompute.PlanningMillisecondsPerUpdate));
    }
    private double _lastTime;
    private long _generation = -1;
    public static Point Sector(Point cell) => new(cell.X / SectorSize, cell.Y / SectorSize);
    public Point? TargetOf(Guid scoutId) => _leases.TryGetValue(scoutId, out var lease) ? lease.Cell : null;
    public void Clean(double now)
    {
        long generation = world.SimulationNetwork?.SessionGeneration ?? -1;
        if (generation != _generation || now < _lastTime) { _leases.Clear(); ClientPlanning.Reset(); _lastClientPlanningTime = double.NaN; }
        _generation = generation; _lastTime = now;
        foreach (var pair in _leases.ToArray())
            if (pair.Value.Until < now || world.Units.FindById(pair.Key) is not MobileUnit unit ||
                unit.IsDying || unit.IsEmbarked || unit.ArmyId != pair.Value.Army) _leases.Remove(pair.Key);
    }
    public bool Reserved(Guid army, Point sector, Guid except) => _leases.Any(pair => pair.Key != except &&
        pair.Value.Army == army && Sector(pair.Value.Cell) == sector);
    public IEnumerable<Point> OtherTargets(Guid army, Guid except) => _leases.Where(pair => pair.Key != except &&
        pair.Value.Army == army).Select(pair => pair.Value.Cell);
    public bool Claim(Guid scout, Guid army, Point target, Guid controller, double now)
    {
        if (Reserved(army, Sector(target), scout)) return false;
        _leases[scout] = new(army, target, controller, now + 12);
        return true;
    }
    public void Release(Guid scout, Guid controller)
    {
        if (_leases.TryGetValue(scout, out var lease) && lease.Controller == controller) _leases.Remove(scout);
    }
    public void ReleaseController(Guid controller)
    {
        foreach (Guid id in _leases.Where(pair => pair.Value.Controller == controller).Select(pair => pair.Key).ToArray())
            _leases.Remove(id);
    }
}
