using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace RTS;

/// <summary>
/// Small host-side tactical layer: visible enemies near an AI base are
/// intercepted by idle combat infantry through the normal player commands.
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

    private readonly PlayerCommandService _commands = new(network, playerId);
    private readonly float _defenseRadiusInCells = defenseRadiusInCells ?? DefenseRadiusInCells;
    private float _thinkElapsed;
    private float _orderElapsed;
    private Guid? _targetId;
    private Guid[] _assignedDefenders = [];

    public bool IsEngaging => _targetId is not null;
    public string LastDecision { get; private set; } = "Watching the base perimeter.";

    public void Reset()
    {
        _thinkElapsed = 0.0f;
        _orderElapsed = 0.0f;
        _targetId = null;
        _assignedDefenders = [];
        LastDecision = "Watching the base perimeter.";
    }

    public void Update(GameTime gameTime, Guid? scoutId)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        _orderElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        Building[] buildings = world.Units.Units.OfType<Building>()
            .Where(building => building.ArmyId == armyId && building.IsCompleted && !building.IsDying)
            .ToArray();
        if (buildings.Length == 0)
            return;

        Unit? target = SelectVisibleThreat(buildings);
        if (target is null)
        {
            if (_targetId is not null)
                _ = ReturnDefendersAsync(buildings);
            _targetId = null;
            _assignedDefenders = [];
            LastDecision = "No visible enemy threatens the base perimeter.";
            return;
        }

        Unit[] defenders = world.Units.Units
            .Where(unit => unit.ArmyId == armyId && unit.UnitId != scoutId &&
                !unit.IsDying && !unit.IsEmbarked && unit.AttackDamage > 0.0f &&
                unit is Soldier or Tank && unit.Occupancy?.IsOperational != false &&
                unit.CanAttackTarget(target))
            .OrderBy(unit => HorizontalDistanceSquared(unit.Position, target.Position))
            .ToArray();
        if (defenders.Length == 0)
        {
            LastDecision = $"Visible threat {ShortId(target.UnitId)} detected, but no defender can engage it.";
            return;
        }

        Guid[] defenderIds = defenders.Select(unit => unit.UnitId).ToArray();
        bool targetChanged = _targetId != target.UnitId;
        bool defendersChanged = !_assignedDefenders.SequenceEqual(defenderIds);
        if (targetChanged || defendersChanged || _orderElapsed >= RefreshOrderSeconds)
        {
            _targetId = target.UnitId;
            _assignedDefenders = defenderIds;
            _orderElapsed = 0.0f;
            _ = EngageAsync(defenderIds, target);
        }
        LastDecision = $"{defenderIds.Length} defender(s) intercept visible threat {ShortId(target.UnitId)}.";
    }

    private Unit? SelectVisibleThreat(Building[] buildings)
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
            .FirstOrDefault();
    }

    private async Task EngageAsync(Guid[] defenderIds, Unit target)
    {
        await _commands.GotoAsync(defenderIds, target.Position);
        await _commands.AttackTargetAsync(defenderIds, target.UnitId);
    }

    private async Task ReturnDefendersAsync(Building[] buildings)
    {
        Guid[] existing = _assignedDefenders
            .Where(id => world.Units.FindById(id) is Soldier unit && !unit.IsDying && !unit.IsEmbarked)
            .ToArray();
        if (existing.Length == 0)
            return;

        Building anchor = buildings.OfType<GDIBase>().FirstOrDefault() ?? buildings[0];
        Vector3 destination = anchor.RallyPoint ?? anchor.Position;
        await _commands.StopAsync(existing);
        await _commands.GotoAsync(existing, destination);
    }

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
