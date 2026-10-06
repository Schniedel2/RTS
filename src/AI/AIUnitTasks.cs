using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace RTS;

public enum AIUnitTask { Unbound, Reserve, Scout, BaseDefender, SquadMember, Escort, Recovery, Economy }
public sealed record AIUnitAssignment(Guid UnitId, Guid ArmyId, AIUnitTask Task, string Owner, int Priority,
    double Until, AIUnitAssignment? Previous = null);

/// <summary>Local arbitration for tactical controllers; gameplay still uses host requests.</summary>
public sealed class AIUnitTasks(GameWorld world)
{
    private readonly Dictionary<Guid, AIUnitAssignment> _assignments = [];
    private double _now;
    private long _generation = -1;
    private bool _initialized;
    private readonly List<Guid> _remove = [];
    public void Update(double now)
    {
        long generation = world.SimulationNetwork?.SessionGeneration ?? -1;
        if (_initialized && (generation != _generation || now < _now)) _assignments.Clear();
        _initialized = true; _generation = generation; _now = now;
        _remove.Clear();
        foreach (var pair in _assignments)
            if (world.Units.FindById(pair.Key) is not Unit unit || unit.IsDying || unit.IsEmbarked ||
                unit.ArmyId != pair.Value.ArmyId || pair.Value.Until < now) _remove.Add(pair.Key);
        foreach (Guid id in _remove) _assignments.Remove(id);
    }
    public AIUnitAssignment? Assignment(Unit unit)
    {
        if (unit.ArmyId is not Guid army) return null;
        if (world.AIOrderQueues.TryGetValue(army, out var queue) && queue.ReservationFor(unit.UnitId) is not null)
            return new(unit.UnitId, army, AIUnitTask.Economy, $"{army}:economy", 100, double.PositiveInfinity);
        if (_assignments.TryGetValue(unit.UnitId, out var assignment)) return assignment;
        if (unit is Soldier soldier && (soldier.SquadLeaderId is not null ||
            unit is SquadLeader && world.Units.Units.OfType<Soldier>().Any(member => member.SquadLeaderId == unit.UnitId)))
            return new(unit.UnitId, army, AIUnitTask.SquadMember, $"{army}:squad", 40, double.PositiveInfinity);
        AIUnitTask idle = GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Harvester) ? AIUnitTask.Economy :
            GameplayCatalog.HasAIRoles(unit.GameplayTypeId, AIUnitRole.Defender) ? AIUnitTask.Reserve : AIUnitTask.Unbound;
        return new(unit.UnitId, army, idle, idle == AIUnitTask.Economy ? $"{army}:economy" : "", idle == AIUnitTask.Economy ? 100 : 0, double.PositiveInfinity);
    }
    public bool CanUse(Unit unit, string owner, int priority)
    {
        if (unit.IsDying || unit.IsEmbarked || unit.ArmyId is null) return false;
        AIUnitAssignment? assigned = Assignment(unit);
        return assigned is null || assigned.Owner == owner || priority > assigned.Priority;
    }
    public bool Claim(Unit unit, string owner, AIUnitTask task, int priority)
    {
        if (!CanUse(unit, owner, priority) || unit.ArmyId is not Guid army) return false;
        AIUnitAssignment? old = Assignment(unit);
        _assignments[unit.UnitId] = new(unit.UnitId, army, task, owner, priority, _now + 15,
            old?.Owner == owner ? old.Previous : old);
        return true;
    }
    public bool Owns(Unit unit, string owner) => Assignment(unit)?.Owner == owner;
    public void Release(Guid id, string owner)
    {
        if (!_assignments.TryGetValue(id, out var assignment) || assignment.Owner != owner) return;
        if (assignment.Previous is AIUnitAssignment previous) _assignments[id] = previous with { Until = _now + 15 };
        else _assignments.Remove(id);
    }
    public void ReleaseOwner(string owner, int maximumPriority)
    {
        foreach (Guid id in _assignments.Where(pair => pair.Value.Owner == owner && pair.Value.Priority <= maximumPriority)
            .Select(pair => pair.Key).ToArray()) Release(id, owner);
    }
    internal void ReleaseArmy(Guid army)
    {
        foreach (Guid id in _assignments.Where(pair => pair.Value.ArmyId == army).Select(pair => pair.Key).ToArray())
            _assignments.Remove(id);
    }
    public void Renew(string owner, double now)
    {
        Update(now);
        foreach (Guid id in _assignments.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
            _assignments[id] = _assignments[id] with { Until = now + 15 };
    }
}

/// <summary>Explicit actor ownership survives asynchronous command continuations.</summary>
public sealed class AIUnitTaskAgent(GameWorld world, string owner, AIUnitTask task, int priority)
{
    public int Priority { get; set; } = priority;
    public bool CanUse(Unit unit) => world.UnitTasks.CanUse(unit, owner, Priority);
    public bool Owns(Unit unit) => world.UnitTasks.Owns(unit, owner);
    public void Release(Guid id)
    {
        world.UnitTasks.Release(id, owner);
        if (world.Units.FindById(id) is SquadLeader)
            foreach (Soldier member in world.Units.Units.OfType<Soldier>())
                if (member.SquadLeaderId == id) world.UnitTasks.Release(member.UnitId, owner);
    }
    public void ReleaseAll() => world.UnitTasks.ReleaseOwner(owner, Priority);
    public void Update(GameTime? time) { if (time is not null) world.UnitTasks.Renew(owner, time.TotalGameTime.TotalSeconds); }
    public bool Authorize(IEnumerable<Guid> ids)
    {
        HashSet<Unit> units = [];
        foreach (Guid id in ids)
        {
            if (world.Units.FindById(id) is not Unit unit) return false;
            units.Add(unit);
            if (unit is Soldier soldier)
            {
                Guid? leader = unit is SquadLeader ? unit.UnitId : soldier.SquadLeaderId;
                if (leader is Guid leaderId)
                    foreach (Unit member in world.Units.Units.OfType<Soldier>().Where(member =>
                        (member.UnitId == leaderId || member.SquadLeaderId == leaderId) &&
                        !member.IsDying && !member.IsEmbarked && member.ArmyId == unit.ArmyId)) units.Add(member);
            }
        }
        if (units.Any(unit => !CanUse(unit))) return false;
        foreach (Unit unit in units) world.UnitTasks.Claim(unit, owner,
            task == AIUnitTask.SquadMember && unit is not Soldier ? AIUnitTask.Escort : task, Priority);
        return true;
    }
}
