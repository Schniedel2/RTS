using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RTS;

public enum AICombatContext
{
    Unknown,
    BaseDefense,
    Offensive,
    Scouting,
    ResourceOperation
}

public sealed record AIThreatSnapshot(
    float AntiInfantryNeed,
    float AntiVehicleNeed,
    float AntiAirNeed,
    float AntiBuildingNeed)
{
    public static readonly AIThreatSnapshot Baseline = new(0.35f, 0.35f, 0.15f, 0.20f);
}

/// <summary>
/// Host-side, match-local memory of observed enemies and own losses. Values fade over time,
/// so one encounter influences production without fixing the strategy for the whole match.
/// </summary>
public sealed record AIEnemyObservation(Guid UnitId, Guid ArmyId, string TypeId, AIUnitRole Roles,
    TargetDomain Domain, ArmorClass Armor, bool Armed, Vector3 Position, double LastSeen,
    AICombatContext Context)
{
    public float Confidence(double now) => now - LastSeen >= AIThreatAssessment.MemoryLifetimeSeconds
        ? 0 : MathF.Pow(0.5f, (float)Math.Max(0, now - LastSeen) / AIThreatAssessment.MemoryHalfLifeSeconds);
}

public sealed class AIThreatAssessment(Guid armyId)
{
    public const float MemoryLifetimeSeconds = 90;
    public const float MemoryHalfLifeSeconds = 30;
    private readonly Dictionary<Guid, AIEnemyObservation> _observations = [];
    private readonly List<Guid> _remove = [];
    private readonly List<Vector3> _basePositions = [], _resourcePositions = [];
    private double _lastTime;
    private long _generation = -1;
    private bool _initialized;
    public IReadOnlyDictionary<Guid, AIEnemyObservation> EnemyKnowledge => _observations;
    public AIThreatSnapshot RememberedThreats { get; private set; } = AIThreatSnapshot.Baseline;
    public float EstimatedEnemyCount { get; private set; }
    private const float ObservationIntervalSeconds = 1.0f;
    private const float LossHalfLifeSeconds = 60.0f;
    private float _observationElapsed;
    private float _lossInfantry;
    private float _lossVehicle;
    private float _lossAir;
    private float _lossBuilding;

    public AIThreatSnapshot Current { get; private set; } = AIThreatSnapshot.Baseline;
    public int RecordedLosses { get; private set; }
    public AICombatContext LastLossContext { get; private set; }

    public void Update(GameTime gameTime, GameWorld world)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.ThreatAssessment");
        double now = gameTime.TotalGameTime.TotalSeconds;
        long generation = (world.SimulationNetwork ?? Globals.Game?.Network)?.SessionGeneration ?? -1;
        if (_initialized && (generation != _generation || now < _lastTime))
        {
            _observations.Clear(); Current = RememberedThreats = AIThreatSnapshot.Baseline;
            EstimatedEnemyCount = 0; _observationElapsed = 0;
            _lossInfantry = _lossVehicle = _lossAir = _lossBuilding = 0;
            RecordedLosses = 0; LastLossContext = AICombatContext.Unknown;
        }
        _initialized = true; _generation = generation; _lastTime = now;
        float elapsed = Math.Max(0.0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        float lossDecay = MathF.Pow(0.5f, elapsed / LossHalfLifeSeconds);
        _lossInfantry *= lossDecay;
        _lossVehicle *= lossDecay;
        _lossAir *= lossDecay;
        _lossBuilding *= lossDecay;

        _observationElapsed += elapsed;
        if (_observationElapsed < ObservationIntervalSeconds)
            return;
        _observationElapsed %= ObservationIntervalSeconds;

        _remove.Clear();
        foreach (var pair in _observations)
            if (pair.Value.Confidence(now) <= 0 || world.AreArmiesAllied(armyId, pair.Value.ArmyId)) _remove.Add(pair.Key);
        foreach (Guid id in _remove) _observations.Remove(id);
        _basePositions.Clear(); _resourcePositions.Clear();
        foreach (Unit own in world.Units.Units)
        {
            if (own.ArmyId != armyId || own.IsDying) continue;
            if (own is Building) _basePositions.Add(own.Position);
            else if (GameplayCatalog.HasAIRoles(own.GameplayTypeId, AIUnitRole.Harvester)) _resourcePositions.Add(own.Position);
        }
        foreach (Unit enemy in world.Units.Units)
        {
            if (enemy.ArmyId is not Guid enemyArmy || enemyArmy == armyId ||
                world.AreArmiesAllied(armyId, enemyArmy) || !CanObserve(world, enemy.Position)) continue;
            if (!enemy.CanBeTargeted || enemy.HitPoints <= 0) { _observations.Remove(enemy.UnitId); continue; }
            var definition = GameplayCatalog.Find(enemy is Building ? PurchasableType.Building : PurchasableType.Unit, enemy.GameplayTypeId);
            _observations[enemy.UnitId] = new(enemy.UnitId, enemyArmy, enemy.GameplayTypeId,
                definition?.AI?.Roles ?? AIUnitRole.None, enemy.Domain, enemy.Armor, enemy.CanFireWeapon,
                enemy.Position, now, ObservationContext(enemy.Position));
        }
        float infantry = 0, vehicle = 0, air = 0, building = 0;
        EstimatedEnemyCount = 0;
        foreach (AIEnemyObservation observation in _observations.Values)
        {
            float confidence = observation.Confidence(now);
            EstimatedEnemyCount += confidence;
            if ((observation.Domain & TargetDomain.Air) != 0) air += 0.45f * confidence;
            else if (observation.Armor == ArmorClass.Infantry) infantry += 0.22f * confidence;
            else if (observation.Armor is ArmorClass.LightVehicle or ArmorClass.HeavyVehicle) vehicle += 0.35f * confidence;
            else if (observation.Armor == ArmorClass.Building && observation.Armed) building += 0.25f * confidence;
        }
        AIThreatSnapshot observed = RememberedThreats = new(ClampNeed(0.35f + infantry), ClampNeed(0.35f + vehicle),
            ClampNeed(0.15f + air), ClampNeed(0.20f + building));
        Current = new(
            Smooth(Current.AntiInfantryNeed, observed.AntiInfantryNeed + _lossInfantry),
            Smooth(Current.AntiVehicleNeed, observed.AntiVehicleNeed + _lossVehicle),
            Smooth(Current.AntiAirNeed, observed.AntiAirNeed + _lossAir),
            Smooth(Current.AntiBuildingNeed, observed.AntiBuildingNeed + _lossBuilding));
    }

    public void RecordLoss(Unit lostUnit, Unit? attacker, AICombatContext context)
    {
        if (lostUnit.ArmyId != armyId)
            return;
        float contextWeight = context switch
        {
            AICombatContext.BaseDefense => 1.35f,
            AICombatContext.ResourceOperation => 1.25f,
            AICombatContext.Offensive => 1.0f,
            AICombatContext.Scouting => 0.65f,
            _ => 0.8f
        };
        AddThreat(attacker, contextWeight);
        RecordedLosses++;
        LastLossContext = context;
    }

    public AIProductionNeed CreateGroundCombatNeed(float infantryBias = 0.0f,
        float vehicleBias = 0.0f) => new(
        AIUnitRole.Attacker,
        AIMovementDomain.GroundVehicle,
        AntiInfantry: Current.AntiInfantryNeed + infantryBias,
        AntiVehicle: Current.AntiVehicleNeed + vehicleBias,
        AntiBuilding: Current.AntiBuildingNeed,
        AntiAir: Current.AntiAirNeed,
        Defense: 0.4f,
        Mobility: 0.2f);

    public AIProductionNeed CreateInfantryCombatNeed(float infantryBias, float vehicleBias) => new(
        AIUnitRole.Attacker,
        AIMovementDomain.Infantry,
        AntiInfantry: Current.AntiInfantryNeed + infantryBias,
        AntiVehicle: Current.AntiVehicleNeed + vehicleBias,
        AntiAir: Current.AntiAirNeed,
        Defense: 0.2f,
        Mobility: 0.15f);

    public static AIThreatSnapshot FromEnemies(Unit[] enemies)
    {
        float infantry = 0.0f, vehicle = 0.0f, air = 0.0f, building = 0.0f;
        foreach (Unit enemy in enemies)
        {
            if (enemy.Domain.HasFlag(TargetDomain.Air)) air += 0.45f;
            else if (enemy.Armor == ArmorClass.Infantry) infantry += 0.22f;
            else if (enemy.Armor is ArmorClass.LightVehicle or ArmorClass.HeavyVehicle) vehicle += 0.35f;
            else if (enemy.Armor == ArmorClass.Building && enemy.CanFireWeapon) building += 0.25f;
        }
        return new(ClampNeed(0.35f + infantry), ClampNeed(0.35f + vehicle),
            ClampNeed(0.15f + air), ClampNeed(0.20f + building));
    }

    private void AddThreat(Unit? attacker, float amount)
    {
        if (attacker?.Domain.HasFlag(TargetDomain.Air) == true) _lossAir += amount;
        else if (attacker?.Armor == ArmorClass.Infantry) _lossInfantry += amount;
        else if (attacker?.Armor is ArmorClass.LightVehicle or ArmorClass.HeavyVehicle) _lossVehicle += amount;
        else if (attacker?.Armor == ArmorClass.Building) _lossBuilding += amount;
        else _lossInfantry += amount * 0.5f;
    }

    private bool CanObserve(GameWorld world, Vector3 position)
    {
        CellVisibility visibility = world.Visibility.GetVisibility(armyId, world.GameGrid.ToCell(position));
        return (visibility & CellVisibility.Visible) != 0 ||
            (world.SimulationArmies.Find(armyId)?.Intelligence.ShareWorldVision == true &&
             (visibility & CellVisibility.VisibleByAlly) != 0);
    }

    private AICombatContext ObservationContext(Vector3 position)
    {
        foreach (Vector3 own in _basePositions)
            if (Vector3.DistanceSquared(own, position) <= 900) return AICombatContext.BaseDefense;
        foreach (Vector3 own in _resourcePositions)
            if (Vector3.DistanceSquared(own, position) <= 900) return AICombatContext.ResourceOperation;
        return AICombatContext.Scouting;
    }

    private static float Smooth(float previous, float next) =>
        MathHelper.Lerp(previous, ClampNeed(next), 0.2f);

    private static float ClampNeed(float value) => Math.Clamp(value, 0.05f, 3.0f);
}
