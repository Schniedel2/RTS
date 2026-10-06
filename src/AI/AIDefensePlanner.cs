using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;

namespace RTS;

public enum AIDefensePlanState
{
    Idle,
    WaitingForResources,
    AssigningPowerCrew,
    TrainingPowerCrew,
    BuildingPower,
    BuildingDefense,
    Ready
}

/// <summary>
/// Turns a threat need into the next host-authoritative construction step.
/// The first supported plan builds catalog-selected static anti-air defense.
/// </summary>
public sealed class AIDefensePlanner(
    GameWorld world,
    Player actor,
    NetworkHandler network,
    AIThreatAssessment threats)
{
    public const float AirThreatThreshold = 0.55f;
    public const float AirThreatPerDefense = 0.75f;
    public const int MaximumAirDefenses = 3;

    public const int PowerHeadroom = 10;
    private const float ThinkIntervalSeconds = 1.0f;
    private const float RequestTimeoutSeconds = 3.0f;
    private readonly PlayerCommandService _commands = new(network, actor.Id);
    private readonly AIProductionPlanExecutor _executor = new(world, actor, network);
    private float _thinkElapsed;
    private float _crewActionElapsed = RequestTimeoutSeconds;
    private float _retryPlanIn;

    public AIDefensePlanState State { get; private set; }
    public bool IsBusy => State is AIDefensePlanState.WaitingForResources or
        AIDefensePlanState.AssigningPowerCrew or AIDefensePlanState.TrainingPowerCrew or
        AIDefensePlanState.BuildingPower or AIDefensePlanState.BuildingDefense || _executor.IsBusy;
    public string LastDecision { get; private set; } = "No adaptive defense plan is required.";

    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.DefensePlanner");
        float elapsed = Math.Max(0.0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _thinkElapsed += elapsed;
        _crewActionElapsed += elapsed;
        _retryPlanIn = Math.Max(0, _retryPlanIn - elapsed);
        _executor.Update(gameTime);
        if (_executor.IsBusy)
        {
            bool buildsPower = _executor.CurrentStep?.Kind == AIProductionPlanStepKind.BuildBuilding &&
                GameplayCatalog.Find(PurchasableType.Building, _executor.CurrentStep.TypeId)?
                    .Building?.PowerProduction > 0;
            State = buildsPower ? AIDefensePlanState.BuildingPower : AIDefensePlanState.BuildingDefense;
            LastDecision = _executor.LastDecision;
            return;
        }
        if (_executor.State == AIPlanExecutionState.Failed)
        {
            LastDecision = _executor.LastDecision;
            State = AIDefensePlanState.Idle;
            _executor.Reset();
            _retryPlanIn = 5;
            return;
        }
        if (_executor.State == AIPlanExecutionState.Completed)
            _executor.Reset();
        if (_retryPlanIn > 0) return;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        if (threats.Current.AntiAirNeed < AirThreatThreshold)
        {
            State = AIDefensePlanState.Idle;
            LastDecision = "Air threat remains below the defense threshold.";
            return;
        }

        var need = new AIProductionNeed(
            AIUnitRole.Defender | AIUnitRole.AntiAir,
            AIMovementDomain.Static,
            AntiAir: threats.Current.AntiAirNeed,
            Defense: 0.5f);
        GameplayDefinition? defense = AIStrategicCatalog.SelectDefense(world, actor.ArmyId, need);
        if (defense?.Building is not BuildingMetadata defenseStats)
        {
            State = AIDefensePlanState.Idle;
            LastDecision = "No builder product in the catalog can counter the air threat.";
            return;
        }

        int desired = DesiredDefenseCount(threats.Current.AntiAirNeed);
        int existing = world.Units.GetArmyUnits(actor.ArmyId).Count(unit => unit.ArmyId == actor.ArmyId &&
            !unit.IsDying && string.Equals(unit.GameplayTypeId, defense.TypeId,
                StringComparison.OrdinalIgnoreCase));
        if (existing >= desired)
        {
            State = AIDefensePlanState.Ready;
            LastDecision = $"Adaptive air defense ready ({existing}/{desired} {defense.DisplayName}).";
            return;
        }

        Army? ownerArmy = world.SimulationArmies.Find(actor.ArmyId);
        if (ownerArmy is null)
        {
            State = AIDefensePlanState.WaitingForResources;
            LastDecision = "Adaptive defense is waiting for its army state.";
            return;
        }

        ArmyPowerStatus power = ArmyPowerStatus.Calculate(world.Units.GetArmyUnits(actor.ArmyId), actor.ArmyId);
        int additionalPower = Math.Max(0,
            defenseStats.PowerConsumption + PowerHeadroom - power.Balance);
        AIPowerSolution crewSolution = AICrewPowerPlanner.Evaluate(
            world, actor.ArmyId, additionalPower);
        if (HandleCrewPowerSolution(crewSolution, ownerArmy))
            return;

        AIProductionPlan plan = AIStrategicCatalog.Plan(world, actor.ArmyId, defense);
        if (!plan.IsValid || plan.NextStep is null)
        {
            State = AIDefensePlanState.Idle;
            LastDecision = plan.Failure ?? "Adaptive defense plan contains no executable step.";
            return;
        }
        _executor.Start(plan);
        _executor.Update(gameTime);
        State = plan.NextStep.Kind == AIProductionPlanStepKind.BuildBuilding &&
            GameplayCatalog.Find(PurchasableType.Building, plan.NextStep.TypeId)?
                .Building?.PowerProduction > 0
            ? AIDefensePlanState.BuildingPower
            : AIDefensePlanState.BuildingDefense;
        LastDecision = _executor.LastDecision;
    }

    private bool HandleCrewPowerSolution(AIPowerSolution solution, Army army)
    {
        switch (solution.Kind)
        {
            case AIPowerSolutionKind.None:
            case AIPowerSolutionKind.BuildPower:
                return false;
            case AIPowerSolutionKind.AssignCrew:
                State = AIDefensePlanState.AssigningPowerCrew;
                LastDecision = $"Assigning existing crew for +{solution.PowerGain} air-defense power.";
                if (_crewActionElapsed >= RequestTimeoutSeconds &&
                    solution.CrewUnitId is Guid crewId && solution.TargetBuildingId is Guid targetId)
                {
                    _ = _commands.EnterUnitAsync(crewId, targetId, OccupantRole.Crew);
                    _crewActionElapsed = 0.0f;
                }
                return true;
            case AIPowerSolutionKind.WaitForCrew:
                State = AIDefensePlanState.AssigningPowerCrew;
                LastDecision = "Waiting for power crew to enter the reactor.";
                return true;
            case AIPowerSolutionKind.TrainCrew:
                State = AIDefensePlanState.TrainingPowerCrew;
                if (solution.ProducerBuildingId is not Guid producerId ||
                    string.IsNullOrWhiteSpace(solution.CrewTypeId))
                    return true;
                PurchaseQuote quote = world.SimulationPricing.GetQuote(new PurchaseRequest(
                    PurchasableType.Unit, solution.CrewTypeId, actor.ArmyId, producerId));
                if (!AIResourcePlanner.CanPropose(world, actor.ArmyId, quote, AIOrderPriority.Power))
                {
                    LastDecision = $"Waiting for {quote.FinalPrice} resources for power crew.";
                    return true;
                }
                LastDecision = $"Training {solution.CrewTypeId} for +{solution.PowerGain} reactor power.";
                if (_crewActionElapsed >= RequestTimeoutSeconds)
                {
                    if (world.AIOrderQueues.TryGetValue(actor.ArmyId, out var queue))
                        queue.Run(AIOrderPriority.Power, () => _commands.TrainUnitAsync(producerId, solution.CrewTypeId));
                    else _ = _commands.TrainUnitAsync(producerId, solution.CrewTypeId);
                    _crewActionElapsed = 0.0f;
                }
                return true;
            default:
                return false;
        }
    }

    public static int DesiredDefenseCount(float antiAirNeed)
    {
        if (!float.IsFinite(antiAirNeed) || antiAirNeed < AirThreatThreshold)
            return 0;
        return Math.Clamp(
            1 + (int)MathF.Floor(
                (antiAirNeed - AirThreatThreshold + 0.0001f) / AirThreatPerDefense),
            1, MaximumAirDefenses);
    }

    private System.Collections.Generic.IReadOnlyDictionary<string, int> CountOwnedBuildings() =>
        world.Units.GetArmyUnits(actor.ArmyId)
            .Where(unit => unit is Building && unit.ArmyId == actor.ArmyId && !unit.IsDying &&
                !string.IsNullOrWhiteSpace(unit.GameplayTypeId))
            .GroupBy(unit => unit.GameplayTypeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

}
