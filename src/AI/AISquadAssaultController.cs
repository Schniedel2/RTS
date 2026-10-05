using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RTS;

public enum AISquadAssaultState
{
    WaitingForSquad,
    WaitingForKnownTarget,
    Staging,
    Advancing,
    Retreating,
    MissionComplete
}

/// <summary>Runs one deliberately simple squad mission against one known enemy building.</summary>
public sealed class AISquadAssaultController(
    GameWorld world,
    Guid playerId,
    Guid armyId,
    NetworkHandler network,
    AIStrategyProfile? strategyProfile = null)
{
    public const float SevereAverageHealthFraction = 0.45f;
    public const int MinimumFightingSoldiers = 2;
    private const float ThinkIntervalSeconds = 0.75f;
    private const float OrderRefreshSeconds = 2.0f;
    private const float StagingRadiusInCells = 8.0f;
    private const float ImmediateThreatRadiusInCells = 14.0f;

    private readonly PlayerCommandService _commands = new(network, playerId);
    private readonly AIStrategyProfile _profile = strategyProfile ??
        AIStrategyProfile.Create(0, armyId);
    private float _thinkElapsed;
    private float _orderElapsed = OrderRefreshSeconds;
    private float _readinessElapsed;
    private float _bestTargetDistance = float.MaxValue;
    private float _lowestTargetHitPoints = float.MaxValue;
    private float _secondsWithoutProgress;
    private Guid? _progressObjectiveId;
    private Guid? _leaderId;
    private Guid? _targetBuildingId;
    private Guid? _issuedAttackTargetId;
    private Guid? _lastTargetBuildingId;
    private Guid[] _escortVehicleIds = [];
    private bool _needsReinforcements;
    private readonly AIProgressWatch _phaseProgress = new();
    private readonly Dictionary<Guid, float> _unreachableTargets = [];
    private float _time;
    private float _progressElapsed;

    public AISquadAssaultState State { get; private set; } = AISquadAssaultState.WaitingForSquad;
    public bool HasActiveMission => _targetBuildingId is not null || State == AISquadAssaultState.Retreating;
    public string LastDecision { get; private set; } = "Waiting for the first squad.";
    public Guid? ReservedScoutId { get; set; }

    public void BeginNextMission()
    {
        State = AISquadAssaultState.WaitingForSquad;
        _leaderId = null;
        _targetBuildingId = null;
        _issuedAttackTargetId = null;
        _needsReinforcements = false;
        _escortVehicleIds = [];
        _readinessElapsed = 0.0f;
        ResetProgressWatch();
        _phaseProgress.Reset();
        _orderElapsed = OrderRefreshSeconds;
        LastDecision = "Recovered squad is preparing its next mission.";
    }

    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.SquadAssault");
        if (State == AISquadAssaultState.MissionComplete)
            return;

        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += elapsed;
        _progressElapsed += elapsed;
        _thinkElapsed += elapsed;
        _orderElapsed += elapsed;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        SquadLeader? leader = _leaderId is Guid missionLeaderId
            ? world.Units.FindById(missionLeaderId) as SquadLeader
            : FindPreparedLeader();
        Soldier[] members = leader is null ? [] : FindLivingMembers(leader).ToArray();
        if (_targetBuildingId is not null && MustRetreat(leader, members, out string reason))
        {
            BeginRetreat(leader, members, reason, needsReinforcements: true);
            return;
        }
        if (State == AISquadAssaultState.Retreating)
        {
            UpdateRetreat(leader, members);
            return;
        }
        if (leader is null)
        {
            State = AISquadAssaultState.WaitingForSquad;
            LastDecision = "Waiting for a living squad leader, medic and three gunners.";
            return;
        }

        if (_targetBuildingId is null && _readinessElapsed < _profile.AttackReadinessSeconds)
        {
            _readinessElapsed += ThinkIntervalSeconds;
            State = AISquadAssaultState.WaitingForKnownTarget;
            LastDecision = $"{_profile.DisplayName} squad is preparing for its next mission " +
                $"({_readinessElapsed:0}/{_profile.AttackReadinessSeconds:0}s).";
            return;
        }

        Building? target = _targetBuildingId is Guid targetId
            ? world.Units.FindById(targetId) as Building
            : null;
        if (target is null || target.IsDying)
        {
            if (_targetBuildingId is not null)
            {
                BeginRetreat(leader, members, "The assigned enemy building was destroyed", needsReinforcements: false);
                return;
            }

            target = SelectKnownTarget();
            if (target is null)
            {
                State = AISquadAssaultState.WaitingForKnownTarget;
                LastDecision = "First squad ready; waiting for the scout to discover an enemy building.";
                return;
            }
            _targetBuildingId = target.UnitId;
            _leaderId = leader.UnitId;
            _escortVehicleIds = FindAvailableCombatEscorts().Select(tank => tank.UnitId).ToArray();
            _orderElapsed = OrderRefreshSeconds;
            ResetProgressWatch();
        }

        Building? home = FindHomeBuilding();
        Vector3 stagingPoint = home?.RallyPoint ?? home?.Position ?? leader.Position;
        if (State is AISquadAssaultState.WaitingForSquad or
            AISquadAssaultState.WaitingForKnownTarget or AISquadAssaultState.Staging)
        {
            float radius = StagingRadiusInCells * world.GameGrid.CellSize;
            bool staged = AllWithin([leader, .. members], stagingPoint, radius);
            if (!staged)
            {
                float gatheringDistance = members.Append<Soldier>(leader)
                    .Max(unit => HorizontalDistanceSquared(unit.Position, stagingPoint));
                if (!_phaseProgress.Update("staging", -MathF.Sqrt(gatheringDistance),
                        _progressElapsed, _profile.AssaultStallTimeoutSeconds, 0.5f))
                {
                    _unreachableTargets[target.UnitId] = _time + AIOrderProgressMonitor.FailureCooldownSeconds;
                    BeginRetreat(leader, members, "Squad staging is unreachable", needsReinforcements: false);
                    _progressElapsed = 0;
                    return;
                }
                _progressElapsed = 0;
                State = AISquadAssaultState.Staging;
                LastDecision = $"Gathering the first squad before attacking {Describe(target)}.";
                if (_orderElapsed >= OrderRefreshSeconds)
                {
                    _ = _commands.GotoAsync([leader.UnitId], stagingPoint);
                    _orderElapsed = 0.0f;
                }
                return;
            }
            State = AISquadAssaultState.Advancing;
            _phaseProgress.Reset();
        }

        MobileUnit[] escortVehicles = FindMissionVehicles();
        Unit? immediateThreat = SelectImmediateThreat(leader, members, escortVehicles);
        if (!UpdateProgressWatch(leader, immediateThreat ?? target))
        {
            _unreachableTargets[target.UnitId] = _time + AIOrderProgressMonitor.FailureCooldownSeconds;
            BeginRetreat(leader, members,
                $"The squad made no progress for {_profile.AssaultStallTimeoutSeconds:0} seconds",
                needsReinforcements: false);
            return;
        }
        Guid desiredTargetId = immediateThreat?.UnitId ?? target.UnitId;
        if (_issuedAttackTargetId != desiredTargetId || _orderElapsed >= OrderRefreshSeconds)
        {
            _issuedAttackTargetId = desiredTargetId;
            _orderElapsed = 0.0f;
            _ = AdvanceAndAttackAsync(
                [leader.UnitId, .. escortVehicles.Select(vehicle => vehicle.UnitId)],
                (immediateThreat ?? target).Position, desiredTargetId);
        }

        LastDecision = immediateThreat is not null
            ? $"First squad with {escortVehicles.Length} escort vehicle(s) is engaging {Describe(immediateThreat)} on the way to {Describe(target)}."
            : $"First squad with {escortVehicles.Length} escort vehicle(s) is advancing on {Describe(target)} " +
                $"(no progress: {_secondsWithoutProgress:0}/{_profile.AssaultStallTimeoutSeconds:0}s).";
    }

    private SquadLeader? FindPreparedLeader() => world.Units.GetArmyUnits(armyId).OfType<SquadLeader>()
        .Where(leader => leader.ArmyId == armyId && !leader.IsDying && !leader.IsEmbarked)
        .OrderByDescending(leader => FindLivingMembers(leader).Count())
        .ThenBy(leader => leader.UnitId)
        .FirstOrDefault(leader =>
            FindLivingMembers(leader).Any(member => HasRole(member, AIUnitRole.Healer)) &&
            FindLivingMembers(leader).Count(member => HasRole(member, AIUnitRole.Attacker) &&
                !HasRole(member, AIUnitRole.Leader) && !HasRole(member, AIUnitRole.Healer)) >=
                _profile.RequiredGunners + _profile.RequiredRakZero);

    private IEnumerable<Soldier> FindLivingMembers(SquadLeader leader) =>
        world.Units.GetArmyUnits(armyId).OfType<Soldier>().Where(member =>
            member.SquadLeaderId == leader.UnitId && member.ArmyId == armyId &&
            !member.IsDying && !member.IsEmbarked);

    private Building? SelectKnownTarget()
    {
        foreach (Guid id in _unreachableTargets.Keys.Where(id => _unreachableTargets[id] <= _time).ToArray())
            _unreachableTargets.Remove(id);
        Building[] candidates = world.Units.Units.OfType<Building>()
            .Where(building => !_unreachableTargets.ContainsKey(building.UnitId))
            .Where(building => building.CanBeTargeted && building.ArmyId != armyId && IsEnemy(building) &&
            world.Visibility.GetDisplayedTerrainVisibility(
                armyId, world.GameGrid.ToCell(building.Position), forMinimap: false) != VisibilityState.Unexplored)
            .OrderBy(GetTargetPriority)
            .ThenBy(building => DistanceToHomeSquared(building.Position))
            .ThenBy(building => building.UnitId)
            .ToArray();
        return candidates.FirstOrDefault(candidate => candidate.UnitId != _lastTargetBuildingId) ??
            candidates.FirstOrDefault();
    }

    private Unit? SelectImmediateThreat(
        SquadLeader leader,
        IReadOnlyCollection<Soldier> members,
        IReadOnlyCollection<MobileUnit> escortVehicles)
    {
        float radius = ImmediateThreatRadiusInCells * world.GameGrid.CellSize;
        float radiusSquared = radius * radius;
        Unit[] attackers = [leader, .. members, .. escortVehicles];
        return world.Units.Units.Where(candidate =>
                candidate is not Building && candidate.CanBeTargeted && IsEnemy(candidate) &&
                HorizontalDistanceSquared(candidate.Position, leader.Position) <= radiusSquared &&
                world.Visibility.GetDisplayedTerrainVisibility(
                    armyId, world.GameGrid.ToCell(candidate.Position), forMinimap: false) == VisibilityState.Visible &&
                attackers.Any(attacker => attacker.CanAttackTarget(candidate) && attacker.AttackDamage > 0.0f))
            .OrderBy(candidate => HorizontalDistanceSquared(candidate.Position, leader.Position))
            .ThenBy(candidate => candidate.UnitId)
            .FirstOrDefault();
    }

    private bool MustRetreat(SquadLeader? leader, Soldier[] members, out string reason)
    {
        if (leader is null || leader.IsDying || leader.IsEmbarked)
        {
            reason = "The squad leader was lost";
            return true;
        }
        if (!members.Any(member => HasRole(member, AIUnitRole.Healer)))
        {
            reason = "The squad medic was lost";
            return true;
        }
        if (members.Count(member => member is not Medic && member.AttackDamage > 0.0f) < MinimumFightingSoldiers)
        {
            reason = "Fewer than two fighting soldiers remain";
            return true;
        }
        float averageHealth = members.Append<Soldier>(leader)
            .Average(member => member.HitPoints / Math.Max(1.0f, member.MaxHitPoints));
        if (averageHealth <= _profile.RetreatHealthFraction)
        {
            reason = $"Squad health fell to {averageHealth * 100.0f:0}%";
            return true;
        }
        reason = string.Empty;
        return false;
    }

    private void BeginRetreat(
        SquadLeader? leader,
        Soldier[] members,
        string reason,
        bool needsReinforcements)
    {
        State = AISquadAssaultState.Retreating;
        if (_targetBuildingId is Guid completedTargetId)
            _lastTargetBuildingId = completedTargetId;
        _targetBuildingId = null;
        _needsReinforcements = needsReinforcements;
        _issuedAttackTargetId = null;
        ResetProgressWatch();
        _orderElapsed = OrderRefreshSeconds;
        LastDecision = $"{reason}; surviving squad members are retreating.";
        IssueRetreat(leader, members);
    }

    private void UpdateRetreat(SquadLeader? leader, Soldier[] members)
    {
        Building? home = FindHomeBuilding();
        if (home is null)
        {
            State = AISquadAssaultState.MissionComplete;
            LastDecision = "Squad mission ended; no home building remains for retreat.";
            return;
        }
        bool leaderSurvives = leader is not null && !leader.IsDying && !leader.IsEmbarked;
        MobileUnit[] escortVehicles = FindMissionVehicles();
        Unit[] survivors = leaderSurvives
            ? [leader!, .. members, .. escortVehicles]
            : [.. members, .. escortVehicles];
        Vector3 destination = home.RallyPoint ?? home.Position;
        float radius = StagingRadiusInCells * world.GameGrid.CellSize;
        if (survivors.Length == 0 || AllWithin(survivors, destination, radius))
        {
            State = AISquadAssaultState.MissionComplete;
            LastDecision = _needsReinforcements
                ? "Surviving squad members returned to base and await reinforcements."
                : "The first squad destroyed its assigned target and returned to base.";
            return;
        }
        float distance = survivors.Max(unit => HorizontalDistanceSquared(unit.Position, destination));
        if (!_phaseProgress.Update("retreat", -MathF.Sqrt(distance), _progressElapsed,
                _profile.AssaultStallTimeoutSeconds, 0.5f))
        {
            _ = _commands.StopAsync(survivors.Select(unit => unit.UnitId));
            State = AISquadAssaultState.MissionComplete;
            LastDecision = "Retreat is unreachable; stopped survivors and released the mission for regrouping.";
            _progressElapsed = 0;
            return;
        }
        _progressElapsed = 0;
        if (_orderElapsed >= OrderRefreshSeconds)
        {
            IssueRetreat(leader, members);
            _orderElapsed = 0.0f;
        }
    }

    private void IssueRetreat(SquadLeader? leader, Soldier[] members)
    {
        Building? home = FindHomeBuilding();
        if (home is null)
            return;
        Guid[] escortIds = FindMissionVehicles().Select(vehicle => vehicle.UnitId).ToArray();
        Guid[] ids = leader is null || leader.IsDying || leader.IsEmbarked
            ? [.. members.Select(member => member.UnitId), .. escortIds]
            : [leader.UnitId, .. escortIds];
        if (ids.Length == 0)
            return;
        _ = RetreatAsync(ids, home.RallyPoint ?? home.Position);
    }

    private async Task AdvanceAndAttackAsync(Guid[] unitIds, Vector3 targetPosition, Guid targetId)
    {
        Unit? target = world.Units.FindById(targetId);
        Guid[] attackers = unitIds.Where(id => world.Units.FindById(id) is Unit unit &&
            target is not null && unit.CanAttackTarget(target)).ToArray();
        // Air-only escorts follow the leader while the squad attacks a building.
        // They receive a combat order when an aircraft enters the local threat area.
        if (_leaderId is Guid leaderId)
        {
            Guid[] followers = unitIds.Except(attackers).Where(id => id != leaderId &&
                world.Units.FindById(id) is Unit unit && unit.FollowUnitId != leaderId).ToArray();
            if (followers.Length > 0) await _commands.FollowAsync(followers, leaderId);
        }
        // Keep existing routes: refreshing the combat target must not run group A* again.
        Guid[] movingIds = attackers.Where(id => world.Units.FindById(id) is MobileUnit unit &&
            NeedsAdvanceOrder(unit, targetPosition)).ToArray();
        if (movingIds.Length > 0)
            await _commands.GotoAsync(movingIds, targetPosition);
        if (attackers.Length > 0) await _commands.AttackTargetAsync(attackers, targetId);
    }

    internal static bool NeedsAdvanceOrder(MobileUnit unit, Vector3 targetPosition) =>
        unit.CurrentCommand is null && unit.PlannedPath.Count == 0 &&
        HorizontalDistanceSquared(unit.Position, targetPosition) > unit.AttackRange * unit.AttackRange;

    private async Task RetreatAsync(Guid[] unitIds, Vector3 destination)
    {
        await _commands.StopAsync(unitIds);
        await _commands.GotoAsync(unitIds, destination);
    }

    private Building? FindHomeBuilding() => world.Units.GetArmyUnits(armyId).OfType<Building>()
        .Where(building => building.ArmyId == armyId && building.IsCompleted && !building.IsDying)
        .OrderBy(building => GameplayCatalog.Find(PurchasableType.Building, building.GameplayTypeId) is GameplayDefinition d
            ? AIStrategicCatalog.Matches(d, AIStrategicBuildingNeed.InfantryProduction) ? 0 :
                AIStrategicCatalog.Matches(d, AIStrategicBuildingNeed.Base) ? 1 : 2 : 2)
        .ThenBy(building => building.UnitId)
        .FirstOrDefault();

    private MobileUnit[] FindAvailableCombatEscorts() => world.Units.GetArmyUnits(armyId).OfType<MobileUnit>()
        .Where(vehicle => vehicle.ArmyId == armyId && !vehicle.IsDying && !vehicle.IsEmbarked &&
            vehicle.UnitId != ReservedScoutId && !vehicle.IsLeavingBuilding &&
            vehicle.Occupancy?.IsOperational != false && IsCombatEscort(vehicle) &&
            (vehicle is not Helicopter helicopter || helicopter.IsReadyForCombatMission))
        .OrderBy(vehicle => vehicle.UnitId)
        .ToArray();

    private MobileUnit[] FindMissionVehicles()
    {
        HashSet<Guid> ids = _escortVehicleIds.ToHashSet();
        return world.Units.GetArmyUnits(armyId).OfType<MobileUnit>()
            .Where(vehicle => ids.Contains(vehicle.UnitId) && vehicle.ArmyId == armyId &&
                !vehicle.IsDying && !vehicle.IsEmbarked && vehicle.Occupancy?.IsOperational != false &&
                IsCombatEscort(vehicle) && vehicle.UnitId != ReservedScoutId &&
                (vehicle is not Helicopter helicopter || helicopter.IsReadyForCombatMission))
            .OrderBy(vehicle => vehicle.UnitId)
            .ToArray();
    }

    private static bool IsCombatEscort(Unit unit) =>
        GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai &&
        ai.Movement is AIMovementDomain.GroundVehicle or AIMovementDomain.Air &&
        ai.Roles.HasFlag(AIUnitRole.Attacker) && (ai.AntiVehicle > 0.0f || ai.AntiAir > 0.0f);

    private static bool HasRole(Unit unit, AIUnitRole roles) =>
        GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai &&
        (ai.Roles & roles) == roles;

    private bool IsEnemy(Unit candidate) => world.Units.GetArmyUnits(armyId)
        .FirstOrDefault(unit => unit.ArmyId == armyId)?.IsEnemy(candidate) == true;

    private float DistanceToHomeSquared(Vector3 position) => FindHomeBuilding() is Building home
        ? HorizontalDistanceSquared(home.Position, position)
        : 0.0f;

    public static int GetTargetPriority(Building building)
    {
        GameplayDefinition? definition = GameplayCatalog.Find(PurchasableType.Building, building.GameplayTypeId);
        if (definition is null) return 4;
        if (definition.AI is { Movement: AIMovementDomain.Static } ai && ai.Roles.HasFlag(AIUnitRole.Defender)) return 0;
        if (GameplayCatalog.GetProducedBy(definition.TypeId).Any(p => p.AI?.Roles.HasFlag(AIUnitRole.Attacker) == true)) return 1;
        if (AIStrategicCatalog.Matches(definition, AIStrategicBuildingNeed.Economy)) return 2;
        if (AIStrategicCatalog.Matches(definition, AIStrategicBuildingNeed.Power)) return 3;
        return 4;
    }

    private static bool AllWithin(IEnumerable<Unit> units, Vector3 center, float radius) =>
        units.All(unit => HorizontalDistanceSquared(unit.Position, center) <= radius * radius);

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }

    private static string Describe(Unit unit) => $"{unit.GetType().Name} {unit.UnitId.ToString("N")[..8]}";

    private bool UpdateProgressWatch(SquadLeader leader, Unit objective)
    {
        if (_progressObjectiveId != objective.UnitId)
        {
            ResetProgressWatch();
            _progressObjectiveId = objective.UnitId;
        }

        float distance = MathF.Sqrt(HorizontalDistanceSquared(leader.Position, objective.Position));
        float meaningfulDistance = Math.Max(0.5f, world.GameGrid.CellSize);
        bool movedCloser = distance <= _bestTargetDistance - meaningfulDistance;
        bool damagedTarget = objective.HitPoints <= _lowestTargetHitPoints - 0.5f;
        if (movedCloser || damagedTarget)
            _secondsWithoutProgress = 0.0f;
        else
            _secondsWithoutProgress += _progressElapsed;
        _progressElapsed = 0;

        _bestTargetDistance = Math.Min(_bestTargetDistance, distance);
        _lowestTargetHitPoints = Math.Min(_lowestTargetHitPoints, objective.HitPoints);
        return _secondsWithoutProgress < _profile.AssaultStallTimeoutSeconds;
    }

    private void ResetProgressWatch()
    {
        _bestTargetDistance = float.MaxValue;
        _lowestTargetHitPoints = float.MaxValue;
        _secondsWithoutProgress = 0.0f;
        _progressObjectiveId = null;
        _progressElapsed = 0;
        _phaseProgress.Reset();
    }
}
