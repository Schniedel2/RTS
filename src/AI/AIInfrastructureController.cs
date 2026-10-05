using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum AIInfrastructureState
{
    MonitoringStorage,
    BuildingStorage,
    AssigningPowerCrew,
    TrainingPowerCrew,
    BuildingPower,
    BuildingVision,
    ResearchingPrerequisite,
    BuildingAirProducer,
    WaitingForAirSupport,
    AirSupportReady
}

/// <summary>Expands storage and maintains a small catalog-selected air-combat force.</summary>
public sealed class AIInfrastructureController(
    GameWorld world,
    Player actor,
    NetworkHandler network)
{
    public const float StorageFreeFractionThreshold = 0.15f;
    public const int OperationalPowerHeadroom = 10;
    public const int DesiredAirCombatUnits = 2;
    private const float ThinkIntervalSeconds = 1.0f;
    private readonly AIProductionPlanExecutor _executor = new(world, actor, network);
    private float _thinkElapsed;
    private bool _airPlan;
    private float _retryPlanIn;
    private float _time;
    private string? _planTarget;
    private readonly Dictionary<string, float> _failedPlans = [];
    private bool PlanCoolingDown(string target) => _failedPlans.GetValueOrDefault(target) > _time;

    public AIInfrastructureState State { get; private set; } = AIInfrastructureState.MonitoringStorage;
    public bool IsAirSupportReady => State == AIInfrastructureState.AirSupportReady;
    public bool HasActivePlan => _executor.IsBusy;
    public bool RequiresImmediatePower =>
        ArmyPowerStatus.Calculate(world.Units.GetArmyUnits(actor.ArmyId), actor.ArmyId).Balance <
            OperationalPowerHeadroom;
    public string LastDecision { get; private set; } = "Monitoring storage and air-technology requirements.";

    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.Infrastructure");
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        _time += elapsed;
        foreach (string key in _failedPlans.Keys.Where(key => _failedPlans[key] <= _time).ToArray())
            _failedPlans.Remove(key);
        _retryPlanIn = Math.Max(0, _retryPlanIn - elapsed);
        // A paused air/research plan must not prevent the power repair that lets it continue.
        if (_executor.IsBusy && RequiresImmediatePower &&
            _executor.CurrentStep?.Kind is AIProductionPlanStepKind.TrainUnit or AIProductionPlanStepKind.Research)
        { _executor.Reset(); _airPlan = false; _retryPlanIn = 0; }
        if (_airPlan && HasAirSupport())
        {
            _executor.Reset();
            _airPlan = false;
        }
        _executor.Update(gameTime);
        if (_executor.IsBusy)
        {
            ReflectExecutorState();
            return;
        }
        if (_executor.State == AIPlanExecutionState.Failed)
        {
            LastDecision = _executor.LastDecision;
            if (_planTarget is string failed) _failedPlans[failed] = _time + AIOrderProgressMonitor.FailureCooldownSeconds;
            _executor.Reset();
            _retryPlanIn = 5;
            return;
        }
        if (_executor.State == AIPlanExecutionState.Completed)
            _executor.Reset();
        if (_retryPlanIn > 0 && !RequiresImmediatePower) return;
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        ArmyPowerStatus power = ArmyPowerStatus.Calculate(world.Units.GetArmyUnits(actor.ArmyId), actor.ArmyId);
        int immediatePowerNeed = Math.Max(0, OperationalPowerHeadroom - power.Balance);
        if (immediatePowerNeed > 0)
        {
            AIPowerSolution solution = AICrewPowerPlanner.Evaluate(
                world, actor.ArmyId, immediatePowerNeed);
            if (HandleCrewPowerSolution(solution, gameTime))
                return;

            GameplayDefinition? powerBuilding = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Power);
            if (powerBuilding is null)
            {
                State = AIInfrastructureState.BuildingPower;
                LastDecision = $"Power deficit is {immediatePowerNeed}, but the catalog has no power producer.";
                return;
            }
            State = AIInfrastructureState.BuildingPower;
            StartCatalogPlan(PurchasableType.Building, powerBuilding.TypeId, gameTime);
            return;
        }

        if (NeedsAdditionalStorage())
        {
            State = AIInfrastructureState.BuildingStorage;
            GameplayDefinition? storageBuilding = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Storage);
            if (storageBuilding is not null && !PlanCoolingDown(storageBuilding.TypeId))
            { StartCatalogPlan(PurchasableType.Building, storageBuilding.TypeId, gameTime); return; }
            else LastDecision = "No feasible storage offer is available.";
        }

        GameplayDefinition? visionBuilding = AIStrategicCatalog.SelectBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Vision);
        if (visionBuilding is not null && !HasOwnedVisionBuilding(visionBuilding.TypeId) && !PlanCoolingDown(visionBuilding.TypeId))
        {
            State = AIInfrastructureState.BuildingVision;
            StartCatalogPlan(PurchasableType.Building, visionBuilding.TypeId, gameTime);
            return;
        }

        GameplayDefinition? airUnit = AIStrategicCatalog.SelectUnit(world, actor.ArmyId,
            new AIProductionNeed(AIUnitRole.Attacker, AIMovementDomain.Air, AntiVehicle: 1, Mobility: .5f));
        if (HasAirSupport())
        {
            State = AIInfrastructureState.AirSupportReady;
            LastDecision = $"Air-combat force ready ({DesiredAirCombatUnits} units).";
            return;
        }
        if (airUnit is null)
        {
            State = AIInfrastructureState.WaitingForAirSupport;
            LastDecision = "No feasible catalog air-support offer is available.";
            return;
        }
        State = AIInfrastructureState.WaitingForAirSupport;
        StartCatalogPlan(PurchasableType.Unit, airUnit.TypeId, gameTime);
    }

    private bool HasAirSupport() => world.Units.GetArmyUnits(actor.ArmyId).Count(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying &&
        GameplayCatalog.Find(PurchasableType.Unit, unit.GameplayTypeId)?.AI is AIUnitMetadata ai &&
        ai.Movement == AIMovementDomain.Air && ai.Roles.HasFlag(AIUnitRole.Attacker)) >= DesiredAirCombatUnits;

    private bool HandleCrewPowerSolution(AIPowerSolution solution, GameTime gameTime)
    {
        switch (solution.Kind)
        {
            case AIPowerSolutionKind.None:
            case AIPowerSolutionKind.BuildPower:
                return false;
            case AIPowerSolutionKind.AssignCrew:
                if (PlanCoolingDown($"crew:{solution.CrewUnitId}:{solution.TargetBuildingId}")) return false;
                State = AIInfrastructureState.AssigningPowerCrew;
                LastDecision = $"Assigning existing crew for +{solution.PowerGain} power.";
                if (solution.CrewUnitId is Guid crewId && solution.TargetBuildingId is Guid targetId)
                {
                    _planTarget = $"crew:{crewId}:{targetId}";
                    StartPlan(new AIProductionPlan(
                        [AIProductionPlanStep.AssignCrew(crewId, targetId)]), gameTime);
                }
                return true;
            case AIPowerSolutionKind.WaitForCrew:
                State = AIInfrastructureState.AssigningPowerCrew;
                LastDecision = "Waiting for power crew to enter a power producer.";
                return true;
            case AIPowerSolutionKind.TrainCrew:
                if (solution.CrewTypeId is string crewType && PlanCoolingDown(crewType)) return false;
                State = AIInfrastructureState.TrainingPowerCrew;
                if (string.IsNullOrWhiteSpace(solution.CrewTypeId))
                    return true;
                LastDecision = $"Training {solution.CrewTypeId} for +{solution.PowerGain} crew power.";
                StartCatalogPlan(PurchasableType.Unit, solution.CrewTypeId, gameTime);
                return true;
            default:
                return false;
        }
    }

    private bool NeedsAdditionalStorage()
    {
        Building[] storage = world.Units.GetArmyUnits(actor.ArmyId).OfType<Building>()
            .Where(building => building.ArmyId == actor.ArmyId && building.IsCompleted &&
                !building.IsDying && building.ResourceCapacity > 0.0f)
            .ToArray();
        if (storage.Length == 0)
            return false;
        float capacity = storage.Sum(building => building.ResourceCapacity);
        float free = storage.Sum(building => building.AvailableResourceCapacity);
        bool storageUnderConstruction = world.Units.GetArmyUnits(actor.ArmyId).OfType<Building>()
            .Any(building => building.ArmyId == actor.ArmyId && !building.IsDying && !building.IsCompleted && building.ResourceCapacity > 0);
        return !storageUnderConstruction && free <= Math.Max(
            Harvester.DefaultCargoCapacity, capacity * StorageFreeFractionThreshold);
    }

    private bool HasOwnedVisionBuilding(string typeId) =>
        world.Units.GetArmyUnits(actor.ArmyId).Any(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying &&
            string.Equals(unit.GameplayTypeId, typeId, StringComparison.OrdinalIgnoreCase));

    private void StartCatalogPlan(PurchasableType type, string typeId, GameTime gameTime)
    {
        if (PlanCoolingDown(typeId))
        { LastDecision = $"Temporarily skipping failed plan {typeId}; other AI work continues."; return; }
        _planTarget = typeId;
        GameplayDefinition? target = GameplayCatalog.Find(type, typeId);
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (target is null || army is null)
        {
            LastDecision = $"Cannot plan {typeId}: catalog or army state is unavailable.";
            return;
        }
        _airPlan = target.Type == PurchasableType.Unit && target.AI?.Movement == AIMovementDomain.Air &&
            target.AI.Roles.HasFlag(AIUnitRole.Attacker);
        AIProductionPlan plan = AIStrategicCatalog.Plan(world, actor.ArmyId, target);
        if (!plan.IsValid || plan.NextStep is null)
        {
            LastDecision = plan.Failure ?? $"No executable production plan for {typeId}.";
            return;
        }
        if (plan.NextStep.Kind == AIProductionPlanStepKind.BuildBuilding &&
            GameplayCatalog.Find(PurchasableType.Building, plan.NextStep.TypeId)?.Building is
                { PowerProduction: > 0 })
        {
            int consumption = plan.Steps.Where(step => step.Kind == AIProductionPlanStepKind.BuildBuilding)
                .Select(step => GameplayCatalog.Find(PurchasableType.Building, step.TypeId)?.Building)
                .Where(stats => stats is not null && stats.PowerProduction <= stats.PowerConsumption)
                .Sum(stats => stats!.PowerConsumption);
            int needed = Math.Max(0, consumption + OperationalPowerHeadroom -
                ArmyPowerStatus.Calculate(world.Units.GetArmyUnits(actor.ArmyId), actor.ArmyId).Balance);
            if (HandleCrewPowerSolution(AICrewPowerPlanner.Evaluate(world, actor.ArmyId, needed), gameTime))
                return;
        }
        StartPlan(plan, gameTime);
    }

    private void StartPlan(AIProductionPlan plan, GameTime gameTime)
    {
        if (_executor.Start(plan))
            _executor.Update(gameTime);
        ReflectExecutorState();
    }

    private void ReflectExecutorState()
    {
        AIProductionPlanStep? step = _executor.CurrentStep;
        State = step?.Kind switch
        {
            AIProductionPlanStepKind.Research => AIInfrastructureState.ResearchingPrerequisite,
            AIProductionPlanStepKind.AssignCrew => AIInfrastructureState.AssigningPowerCrew,
            AIProductionPlanStepKind.TrainUnit when
                GameplayCatalog.Find(PurchasableType.Unit, step.TypeId)?.AI?.Movement == AIMovementDomain.Air =>
                    AIInfrastructureState.WaitingForAirSupport,
            AIProductionPlanStepKind.TrainUnit => AIInfrastructureState.TrainingPowerCrew,
            AIProductionPlanStepKind.BuildBuilding when
                GameplayCatalog.Find(PurchasableType.Building, step.TypeId)?.Building?.ResourceCapacity > 0 =>
                    AIInfrastructureState.BuildingStorage,
            AIProductionPlanStepKind.BuildBuilding when
                GameplayCatalog.GetProducedBy(step.TypeId).Any(product => product.AI?.Movement == AIMovementDomain.Air) =>
                    AIInfrastructureState.BuildingAirProducer,
            AIProductionPlanStepKind.BuildBuilding when
                GameplayCatalog.Find(PurchasableType.Building, step.TypeId)?
                    .Building?.VisionRange > 0 =>
                    AIInfrastructureState.BuildingVision,
            AIProductionPlanStepKind.BuildBuilding => AIInfrastructureState.BuildingPower,
            _ => AIInfrastructureState.MonitoringStorage
        };
        LastDecision = _executor.LastDecision;
    }
}
