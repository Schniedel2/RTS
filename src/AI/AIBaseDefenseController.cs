using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTS;

/// <summary>
/// Small host-side tactical layer: visible enemies near an AI base are
/// intercepted by a proportionate group of suitable mobile defenders through the normal player commands.
/// </summary>
public sealed class AIBaseDefenseController(
    GameWorld world,
    Guid playerId,
    Guid armyId,
    NetworkHandler network,
    float? defenseRadiusInCells = null)
{
    public const float DefenseRadiusInCells = 36.0f;
    private const float ThinkIntervalSeconds = 0.75f;
    private const float RefreshOrderSeconds = 2.0f;

    private readonly PlayerCommandService _commands = new(network, playerId, new AIUnitTaskAgent(world, $"{armyId}:defense", AIUnitTask.BaseDefender, 60));
    private readonly float _defenseRadiusInCells = defenseRadiusInCells ?? DefenseRadiusInCells;
    private float _thinkElapsed;
    private float _orderElapsed;
    private Guid? _targetId;
    private Guid[] _assignedDefenders = [];
    private readonly Dictionary<Guid, DefenseReturnOrder> _returnOrders = [];

    private sealed record DefenseReturnOrder(
        Guid UnitId,
        Vector3 Position,
        Vector2? MovementTarget,
        Guid? FollowTargetId,
        Guid? AttackTargetId,
        Vector3? AttackGroundTarget);

    public float RequiredDefensePower { get; private set; }
    public float AssignedDefensePower { get; private set; }
    public IReadOnlyList<Guid> AssignedDefenders => _assignedDefenders;
    public bool IsEngaging => _targetId is not null;
    public string LastDecision { get; private set; } = "Watching the base perimeter.";

    public void Reset()
    {
        foreach (Guid id in _assignedDefenders) _commands.TaskAgent!.Release(id);
        _thinkElapsed = 0.0f;
        _orderElapsed = 0.0f;
        _targetId = null;
        _assignedDefenders = [];
        _returnOrders.Clear();
        RequiredDefensePower = AssignedDefensePower = 0;
        LastDecision = "Watching the base perimeter.";
    }

    public void Update(GameTime gameTime, Guid? scoutId)
    {
        _commands.TaskAgent!.Update(gameTime);
        using var measurement = PerformanceMeasurements.Measure("AI.BaseDefense");
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        _orderElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        Building[] buildings = world.Units.GetArmyUnits(armyId).OfType<Building>()
            .Where(building => building.ArmyId == armyId && building.IsCompleted && !building.IsDying)
            .ToArray();
        Unit[] threats = SelectVisibleThreats(buildings);
        Unit? target = threats.FirstOrDefault();
        if (target is null)
        {
            if (_targetId is not null)
                _ = ReturnDefendersAsync(buildings);
            _targetId = null;
            _assignedDefenders = [];
            _returnOrders.Clear();
            RequiredDefensePower = AssignedDefensePower = 0;
            LastDecision = "No visible enemy threatens the base perimeter.";
            return;
        }

        Unit[] available = world.Units.GetArmyUnits(armyId)
            .Where(unit => unit.ArmyId == armyId && unit.UnitId != scoutId &&
                !unit.IsDying && !unit.IsEmbarked && unit.AttackDamage > 0.0f &&
                unit is MobileUnit && (unit is not Soldier soldier || soldier.SquadLeaderId is null) && GameplayCatalog.HasAIRoles(
                    unit.GameplayTypeId, AIUnitRole.Defender) &&
                unit.Occupancy?.IsOperational != false &&
                unit.CanAttackTarget(target) && _commands.TaskAgent!.CanUse(unit))
            .ToArray();
        // Only the local incident contributes force demand. Separate simultaneous incidents
        // remain a later TODO; distant sides of the base are not summed into this target.
        Unit[] localThreats = threats.Where(enemy => HorizontalDistanceSquared(enemy.Position, target.Position) <=
            12 * 12 * world.GameGrid.CellSize * world.GameGrid.CellSize).ToArray();
        AIDefenseForce force = AIDefenseForceSelector.Select(available, localThreats, target,
            world.GameGrid.CellSize, _assignedDefenders);
        Unit[] defenders = force.Defenders;
        RequiredDefensePower = force.RequiredPower; AssignedDefensePower = force.AssignedPower;
        Guid[] released = _assignedDefenders.Where(id => !defenders.Any(unit => unit.UnitId == id)).ToArray();
        if (released.Length > 0)
        {
            _ = ReturnDefendersAsync(buildings, released);
            foreach (Guid id in released) _returnOrders.Remove(id);
        }
        if (defenders.Length == 0)
        {
            _targetId = null; _assignedDefenders = [];
            LastDecision = $"Visible threat {ShortId(target.UnitId)} detected, but no defender can engage it.";
            return;
        }

        Guid[] defenderIds = defenders.Select(unit => unit.UnitId).ToArray();
        bool targetChanged = _targetId != target.UnitId;
        bool defendersChanged = !_assignedDefenders.SequenceEqual(defenderIds);
        if (targetChanged || defendersChanged || _orderElapsed >= RefreshOrderSeconds)
        {
            foreach (Unit defender in defenders)
                _returnOrders.TryAdd(defender.UnitId, CaptureReturnOrder(defender));
            _targetId = target.UnitId;
            _assignedDefenders = defenderIds;
            _orderElapsed = 0.0f;
            _ = EngageAsync(defenders, target, targetChanged || defendersChanged);
        }
        LastDecision = $"{defenderIds.Length} defender(s) intercept visible threat {ShortId(target.UnitId)}; strength {AssignedDefensePower:0.0}/{RequiredDefensePower:0.0}.";
    }

    private Unit[] SelectVisibleThreats(Building[] buildings)
    {
        float radius = _defenseRadiusInCells * world.GameGrid.CellSize;
        float radiusSquared = radius * radius;
        return world.Units.Units
            .Where(candidate => candidate.CanBeTargeted && candidate.ArmyId != armyId &&
                buildings.Any(building => HorizontalDistanceSquared(building.Position, candidate.Position) <= radiusSquared) &&
                world.Visibility.GetDisplayedTerrainVisibility(
                    armyId, world.GameGrid.ToCell(candidate.Position), forMinimap: false) == VisibilityState.Visible)
            .Where(candidate => buildings.Any(building => building.IsEnemy(candidate)))
            .OrderBy(candidate => buildings.Min(building => HorizontalDistanceSquared(building.Position, candidate.Position)))
            .ThenBy(candidate => candidate.UnitId)
            .ToArray();
    }

    private async Task EngageAsync(Unit[] defenders, Unit target, bool assignmentChanged)
    {
        // Attack orders are refreshed periodically, but an already travelling
        // defender does not need another complete A* route every two seconds.
        // Re-route the whole group when its assignment changes; afterwards only
        // units which have exhausted their route need a new approach order.
        Guid[] movingIds = defenders
            .Where(unit => assignmentChanged || unit.CurrentCommand is null &&
                (unit is not MobileUnit mobile || mobile.PlannedPath.Count == 0))
            .Select(unit => unit.UnitId)
            .ToArray();
        if (movingIds.Length > 0)
            await _commands.GotoAsync(movingIds, target.Position);
        Guid[] defenderIds = defenders.Select(unit => unit.UnitId).ToArray();
        await _commands.AttackTargetAsync(defenderIds, target.UnitId);
    }

    private async Task ReturnDefendersAsync(Building[] buildings, Guid[]? only = null)
    {
        DefenseReturnOrder[] returning = _returnOrders.Values
            .Where(order => (only is null || only.Contains(order.UnitId)) && world.Units.FindById(order.UnitId) is Unit unit &&
                IsAvailableReturningDefender(unit) && _commands.TaskAgent!.Owns(unit))
            .ToArray();
        if (returning.Length == 0)
            return;

        Building? anchor = buildings.FirstOrDefault(b => GameplayCatalog.Find(PurchasableType.Building,
            b.GameplayTypeId) is GameplayDefinition d && AIStrategicCatalog.Matches(d, AIStrategicBuildingNeed.Base)) ?? buildings.FirstOrDefault();
        Vector3 fallback = anchor?.RallyPoint ?? anchor?.Position ?? returning[0].Position;
        await _commands.StopAsync(returning.Select(order => order.UnitId));
        foreach (DefenseReturnOrder order in returning)
        {
            if (order.AttackTargetId is Guid attackId &&
                world.Units.FindById(attackId) is Unit { IsDying: false })
                await _commands.AttackTargetAsync([order.UnitId], attackId);
            else if (order.FollowTargetId is Guid followId &&
                world.Units.FindById(followId) is Unit { IsDying: false })
                await _commands.FollowAsync([order.UnitId], followId);
            else if (order.AttackGroundTarget is Vector3 groundTarget)
                await _commands.AttackTerrainAsync([order.UnitId], groundTarget);
            else
            {
                Vector2 target = order.MovementTarget ?? new Vector2(order.Position.X, order.Position.Z);
                Vector3 destination = float.IsFinite(target.X) && float.IsFinite(target.Y)
                    ? new Vector3(target.X, order.Position.Y, target.Y)
                    : fallback;
                await _commands.GotoAsync([order.UnitId], destination);
            }
            _commands.TaskAgent!.Release(order.UnitId);
        }
    }

    private static DefenseReturnOrder CaptureReturnOrder(Unit unit) => new(
        unit.UnitId,
        unit.Position,
        unit.CurrentCommand?.Target,
        unit.FollowUnitId,
        unit.AttackTargetId,
        unit.AttackGroundTarget);

    private static bool IsAvailableReturningDefender(Unit unit) =>
        unit is MobileUnit && !unit.IsDying && !unit.IsEmbarked;

    public static bool IsWithinDefenseRadius(Vector3 building, Vector3 target, float cellSize)
    {
        float radius = DefenseRadiusInCells * cellSize;
        return HorizontalDistanceSquared(building, target) <= radius * radius;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8];
}
