using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Linq;

namespace RTS;

public enum AIInfrastructureState
{
    MonitoringStorage,
    BuildingSilo,
    AssigningPowerCrew,
    TrainingPowerCrew,
    BuildingPower,
    BuildingVision,
    ResearchingAirTechnology,
    BuildingHelipad,
    WaitingForHelicopter,
    AirSupportReady
}

/// <summary>Expands storage when needed and establishes the AI's first air-support base.</summary>
public sealed class AIInfrastructureController(
    GameWorld world,
    Player actor,
    NetworkHandler network)
{
    public const float StorageFreeFractionThreshold = 0.15f;
    public const int HelipadPowerHeadroom = 15;
    private const float ThinkIntervalSeconds = 1.0f;
    private readonly AIProductionPlanExecutor _executor = new(world, actor, network);
    private float _thinkElapsed;

    public AIInfrastructureState State { get; private set; } = AIInfrastructureState.MonitoringStorage;
    public bool IsAirSupportReady => State == AIInfrastructureState.AirSupportReady;
    public string LastDecision { get; private set; } = "Monitoring storage and air-technology requirements.";

    public void Update(GameTime gameTime)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _thinkElapsed += elapsed;
        _executor.Update(gameTime);
        if (_executor.IsBusy)
        {
            ReflectExecutorState();
            return;
        }
        if (_executor.State is AIPlanExecutionState.Completed or AIPlanExecutionState.Failed)
            _executor.Reset();
        if (_thinkElapsed < ThinkIntervalSeconds)
            return;
        _thinkElapsed %= ThinkIntervalSeconds;

        if (NeedsAdditionalStorage())
        {
            State = AIInfrastructureState.BuildingSilo;
            StartCatalogPlan(PurchasableType.Building, "silo", gameTime);
            return;
        }

        GameplayDefinition? visionBuilding = FindPreferredVisionBuilding();
        if (visionBuilding is not null && !HasOwnedVisionBuilding(visionBuilding.TypeId))
        {
            State = AIInfrastructureState.BuildingVision;
            StartCatalogPlan(PurchasableType.Building, visionBuilding.TypeId, gameTime);
            return;
        }

        ArmyPowerStatus power = ArmyPowerStatus.Calculate(world.Units.Units, actor.ArmyId);
        Helipad? helipad = world.Units.Units.OfType<Helipad>()
            .FirstOrDefault(building => building.ArmyId == actor.ArmyId && !building.IsDying);
        int helipadConsumption = helipad is null
            ? GameplayCatalog.Find(PurchasableType.Building, "helipad")?.Building?.PowerConsumption ?? 0
            : 0;
        int additionalPower = Math.Max(0,
            helipadConsumption + HelipadPowerHeadroom - power.Balance);
        if (additionalPower > 0)
        {
            AIPowerSolution solution = AICrewPowerPlanner.Evaluate(
                world, actor.ArmyId, additionalPower);
            if (HandleCrewPowerSolution(solution, gameTime))
                return;
            State = AIInfrastructureState.BuildingPower;
            StartCatalogPlan(PurchasableType.Building, "reaktor", gameTime);
            return;
        }

        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (helipad is null)
        {
            State = army?.Perks.Has(PerkType.AirTechnology) == true
                ? AIInfrastructureState.BuildingHelipad
                : AIInfrastructureState.ResearchingAirTechnology;
            StartCatalogPlan(PurchasableType.Building, "helipad", gameTime);
            return;
        }
        if (!helipad.IsCompleted)
        {
            State = AIInfrastructureState.BuildingHelipad;
            LastDecision = $"Constructing helipad ({helipad.ConstructionPercentage * 100.0f:0}%).";
            return;
        }

        Helicopter? helicopter = world.Units.Units.OfType<Helicopter>()
            .FirstOrDefault(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying);
        if (helicopter is null)
        {
            State = AIInfrastructureState.WaitingForHelicopter;
            LastDecision = "Helipad complete; waiting for its included helicopter delivery.";
            return;
        }

        State = AIInfrastructureState.AirSupportReady;
        LastDecision = "Air Technology, powered helipad and first helicopter are ready.";
    }

    private bool HandleCrewPowerSolution(AIPowerSolution solution, GameTime gameTime)
    {
        switch (solution.Kind)
        {
            case AIPowerSolutionKind.None:
            case AIPowerSolutionKind.BuildPower:
                return false;
            case AIPowerSolutionKind.AssignCrew:
                State = AIInfrastructureState.AssigningPowerCrew;
                LastDecision = $"Assigning existing crew for +{solution.PowerGain} power.";
                if (solution.CrewUnitId is Guid crewId && solution.TargetBuildingId is Guid targetId)
                    StartPlan(new AIProductionPlan(
                        [AIProductionPlanStep.AssignCrew(crewId, targetId)]), gameTime);
                return true;
            case AIPowerSolutionKind.WaitForCrew:
                State = AIInfrastructureState.AssigningPowerCrew;
                LastDecision = "Waiting for power crew to enter the reactor.";
                return true;
            case AIPowerSolutionKind.TrainCrew:
                State = AIInfrastructureState.TrainingPowerCrew;
                if (string.IsNullOrWhiteSpace(solution.CrewTypeId))
                    return true;
                LastDecision = $"Training {solution.CrewTypeId} for +{solution.PowerGain} reactor power.";
                StartCatalogPlan(PurchasableType.Unit, solution.CrewTypeId, gameTime);
                return true;
            default:
                return false;
        }
    }

    private bool NeedsAdditionalStorage()
    {
        Building[] storage = world.Units.Units.OfType<Building>()
            .Where(building => building.ArmyId == actor.ArmyId && building.IsCompleted &&
                !building.IsDying && building.ResourceCapacity > 0.0f)
            .ToArray();
        if (storage.Length == 0)
            return false;
        float capacity = storage.Sum(building => building.ResourceCapacity);
        float free = storage.Sum(building => building.AvailableResourceCapacity);
        bool siloUnderConstruction = world.Units.Units.OfType<Silo>()
            .Any(silo => silo.ArmyId == actor.ArmyId && !silo.IsDying && !silo.IsCompleted);
        return !siloUnderConstruction && free <= Math.Max(
            Harvester.DefaultCargoCapacity, capacity * StorageFreeFractionThreshold);
    }

    private static GameplayDefinition? FindPreferredVisionBuilding() =>
        GameplayCatalog.All
            .Where(definition => definition.Type == PurchasableType.Building &&
                definition.Building?.VisionRange > 0 &&
                definition.Producers.Any(producer => string.Equals(
                    producer.TypeId, "gdi-bulldozer", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(definition => definition.Building!.VisionRange)
            .ThenBy(definition => definition.BasePrice)
            .FirstOrDefault();

    private bool HasOwnedVisionBuilding(string typeId) =>
        world.Units.Units.Any(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying &&
            string.Equals(unit.GameplayTypeId, typeId, StringComparison.OrdinalIgnoreCase));

    private void StartCatalogPlan(PurchasableType type, string typeId, GameTime gameTime)
    {
        GameplayDefinition? target = GameplayCatalog.Find(type, typeId);
        Army? army = Globals.Game.Armies.Find(actor.ArmyId);
        if (target is null || army is null)
        {
            LastDecision = $"Cannot plan {typeId}: catalog or army state is unavailable.";
            return;
        }
        string[] ownedTypes = world.Units.Units
            .Where(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying &&
                (unit is not Building building || building.IsCompleted))
            .Select(unit => unit.GameplayTypeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int powerBalance = ArmyPowerStatus.Calculate(world.Units.Units, actor.ArmyId).Balance;
        AIProductionPlan plan = AIProductionPlanner.CreatePlan(
            target, ownedTypes, army.Perks.ActivePerks, powerBalance, HelipadPowerHeadroom);
        if (!plan.IsValid || plan.NextStep is null)
        {
            LastDecision = plan.Failure ?? $"No executable production plan for {typeId}.";
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
            AIProductionPlanStepKind.Research => AIInfrastructureState.ResearchingAirTechnology,
            AIProductionPlanStepKind.AssignCrew => AIInfrastructureState.AssigningPowerCrew,
            AIProductionPlanStepKind.TrainUnit => AIInfrastructureState.TrainingPowerCrew,
            AIProductionPlanStepKind.BuildBuilding when
                string.Equals(step.TypeId, "silo", StringComparison.OrdinalIgnoreCase) =>
                    AIInfrastructureState.BuildingSilo,
            AIProductionPlanStepKind.BuildBuilding when
                string.Equals(step.TypeId, "helipad", StringComparison.OrdinalIgnoreCase) =>
                    AIInfrastructureState.BuildingHelipad,
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
