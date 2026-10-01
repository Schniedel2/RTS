using Microsoft.Xna.Framework;
using System;
using System.Linq;

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
public sealed class AIThreatAssessment(Guid armyId)
{
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

        Unit[] visibleEnemies = world.Units.Units.Where(unit =>
            unit.CanBeTargeted && unit.ArmyId != armyId && IsEnemy(world, unit) &&
            world.Visibility.GetDisplayedTerrainVisibility(
                armyId, world.GameGrid.ToCell(unit.Position), forMinimap: false) == VisibilityState.Visible)
            .ToArray();

        AIThreatSnapshot observed = FromEnemies(visibleEnemies);
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

    private bool IsEnemy(GameWorld world, Unit candidate) => world.Units.Units
        .FirstOrDefault(unit => unit.ArmyId == armyId)?.IsEnemy(candidate) == true;

    private static float Smooth(float previous, float next) =>
        MathHelper.Lerp(previous, ClampNeed(next), 0.2f);

    private static float ClampNeed(float value) => Math.Clamp(value, 0.05f, 3.0f);
}
