using Microsoft.Xna.Framework;
using RTS.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum AIPlanExecutionState
{
    Idle,
    WaitingForResources,
    WaitingForProducer,
    WaitingForHost,
    InProgress,
    Completed,
    Failed
}

/// <summary>Executes one dependency plan at a time through ordinary player network commands.</summary>
public sealed class AIProductionPlanExecutor(
    GameWorld world,
    Player actor,
    NetworkHandler network)
{

    private const float RetrySeconds = 3.0f;
    private readonly PlayerCommandService _commands = new(network, actor.Id);
    private AIProductionPlan? _plan;
    private int _stepIndex;
    private float _retryElapsed = RetrySeconds;
    private Guid? _requestedBuildingId;
    private bool _requestSent;
    private Guid? _adoptedProductionOrderId;
    private readonly AIProgressWatch _progress = new();
    private LocalRequestReceipt? _receipt;
    private Guid? _activeProducerId;
    private bool _wasAcknowledged;
    private Guid? _observedProductionHead;
    public const float StallTimeoutSeconds = 60;

    public AIPlanExecutionState State { get; private set; } = AIPlanExecutionState.Idle;
    public bool IsBusy => State is not AIPlanExecutionState.Idle and
        not AIPlanExecutionState.Completed and not AIPlanExecutionState.Failed;
    public AIProductionPlanStep? CurrentStep =>
        _plan is not null && _stepIndex >= 0 && _stepIndex < _plan.Steps.Count
            ? _plan.Steps[_stepIndex]
            : null;
    public string LastDecision { get; private set; } = "No production plan is active.";

    public bool Start(AIProductionPlan plan)
    {
        if (IsBusy || !plan.IsValid || plan.Steps.Count == 0)
            return false;
        _plan = plan;
        _stepIndex = 0;
        ResetStepState();
        State = AIPlanExecutionState.InProgress;
        LastDecision = $"Started production plan with {plan.Steps.Count} step(s).";
        return true;
    }

    public void Reset()
    {
        _plan = null;
        _stepIndex = 0;
        ResetStepState();
        State = AIPlanExecutionState.Idle;
        LastDecision = "No production plan is active.";
    }

    public void Update(GameTime gameTime)
    {
        using var measurement = PerformanceMeasurements.Measure("AI.ProductionPlan");
        if (!IsBusy || CurrentStep is not AIProductionPlanStep step)
            return;
        float elapsed = Math.Max(0.0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _retryElapsed += elapsed;
        if (_receipt is not null && _receipt.Generation != network.SessionGeneration)
        { Fail("Host session changed; abandoning the old plan.", AIOrderFailure.SessionChanged); return; }

        if (IsStepComplete(step))
        {
            AdvanceStep();
            return;
        }
        if (_requestedBuildingId is Guid pausedSite && world.AIOrderQueues.TryGetValue(actor.ArmyId, out var queue)
            && queue.IsPaused(pausedSite))
        {
            _progress.Reset();
            State = AIPlanExecutionState.WaitingForProducer;
            LastDecision = $"Construction of {step.TypeId} is paused for a higher-priority AI order.";
            return;
        }
        if (_receipt?.Result.Status is AIOrderStatus.Rejected or AIOrderStatus.Failed)
        { Fail($"Host rejected or failed {step.TypeId}: {_receipt.Result.Failure}: {_receipt.Result.Reason}"); return; }
        if (_receipt?.State is LocalRequestState.Rejected or LocalRequestState.Abandoned)
        { Fail($"Host rejected {step.TypeId}: {_receipt.Reason}"); return; }
        // Host queue latency is not a failed gameplay job. Keep one outstanding request.
        if (_receipt?.State == LocalRequestState.Pending)
        {
            State = AIPlanExecutionState.WaitingForHost;
            LastDecision = $"Waiting for host processing of {step.TypeId}; no duplicate is sent.";
            return;
        }
        if (!_progress.Update($"{_stepIndex}:{step.Kind}:{step.TypeId}", GetProgress(step), elapsed, StallTimeoutSeconds))
        { Fail($"No progress on {step.Kind} {step.TypeId} for {StallTimeoutSeconds:0}s; releasing the plan for recovery.", AIOrderFailure.Timeout); return; }
        if (_requestSent && IsStepAcknowledged(step))
        {
            _wasAcknowledged = true;
            if (step.Kind == AIProductionPlanStepKind.BuildBuilding)
                EnsureAcknowledgedBuildingHasWorker(step);
            State = AIPlanExecutionState.InProgress;
            return;
        }
        if (_wasAcknowledged)
        { Fail($"Confirmed {step.TypeId} disappeared or was cancelled before completion; replanning is required."); return; }
        if (_requestSent && _retryElapsed < RetrySeconds)
        {
            State = AIPlanExecutionState.WaitingForHost;
            return;
        }

        Execute(step);
    }

    private void Execute(AIProductionPlanStep step)
    {
        switch (step.Kind)
        {
            case AIProductionPlanStepKind.BuildBuilding:
                ExecuteBuild(step);
                break;
            case AIProductionPlanStepKind.TrainUnit:
                ExecuteTraining(step);
                break;
            case AIProductionPlanStepKind.Research:
                ExecuteResearch(step);
                break;
            case AIProductionPlanStepKind.AssignCrew:
                ExecuteCrewAssignment(step);
                break;
            default:
                Fail($"Unsupported plan step {step.Kind}.");
                break;
        }
    }

    private void ExecuteBuild(AIProductionPlanStep step)
    {
        MobileUnit? builder = world.Units.GetArmyUnits(actor.ArmyId).OfType<MobileUnit>()
            .Where(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying && !unit.IsEmbarked && !unit.IsLeavingBuilding &&
                unit.Occupancy?.IsOperational != false && unit.BuildRate > 0.0f && string.Equals(unit.GameplayTypeId,
                    step.ProducerTypeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(unit => world.AIOrderQueues.TryGetValue(actor.ArmyId, out var reservations) &&
                !reservations.CanUse(unit.UnitId, _requestedBuildingId))
            .ThenBy(unit => unit.UnitId)
            .FirstOrDefault();
        if (builder is null)
        {
            State = AIPlanExecutionState.WaitingForProducer;
            LastDecision = $"Waiting for builder {step.ProducerTypeId} to build {step.TypeId}.";
            return;
        }

        Building? existing = world.Units.GetArmyUnits(actor.ArmyId).OfType<Building>()
            .FirstOrDefault(site => !site.IsDying && !site.IsCompleted &&
                GameplayCatalog.Canonicalize(site.GameplayTypeId) == GameplayCatalog.Canonicalize(step.TypeId));
        if (existing is not null)
        {
            _requestedBuildingId = existing.UnitId;
            _requestSent = true;
            EnsureAcknowledgedBuildingHasWorker(step);
            State = AIPlanExecutionState.InProgress;
            LastDecision = $"Adopted existing construction of {step.TypeId}.";
            return;
        }
        PurchaseQuote quote = Quote(PurchasableType.Building, step.TypeId, builder.UnitId);
        if (!CanPay(quote))
        {
            WaitForResources(step, quote.FinalPrice);
            return;
        }
        Building? preview = BuildingFactory.SpawnBuilding(
            step.TypeId, Vector3.Zero, 0.0f, Guid.NewGuid(), actor.Id, quote.FinalPrice);
        if (preview is null)
        {
            Fail($"Building factory cannot create {step.TypeId}.");
            return;
        }
        ArmyGoalController.PreparePreview(preview, actor);
        Building? home = AIStrategicCatalog.FindBuilding(world, actor.ArmyId, AIStrategicBuildingNeed.Base);
        if (!ArmyGoalController.TryFindBuildingSite(
                world, preview, home?.Position ?? builder.Position, 4, 24, out Vector3 position))
        {
            State = AIPlanExecutionState.WaitingForProducer;
            LastDecision = $"No valid site for {step.TypeId}; retrying.";
            return;
        }

        _requestedBuildingId = Guid.NewGuid();
        _ = _commands.BuildAndConstructAsync(step.TypeId, position, 0.0f,
            [builder.UnitId], _requestedBuildingId.Value);
        _requestedBuildingId = _commands.LastRequest?.Request.UnitId ?? _requestedBuildingId;
        MarkRequest($"Requested {step.TypeId} at ({position.X:0.0}, {position.Z:0.0}).");
    }

    private void ExecuteTraining(AIProductionPlanStep step)
    {
        Building? producer = FindProducer(step);
        if (producer is null)
        {
            State = AIPlanExecutionState.WaitingForProducer;
            LastDecision = $"Waiting for producer {step.ProducerTypeId} to train {step.TypeId}.";
            return;
        }
        if (producer.ProductionQueue.Orders.Any(order => GameplayCatalog.Canonicalize(order.UnitTypeId) == step.TypeId))
        {
            _activeProducerId = producer.UnitId;
            _adoptedProductionOrderId = producer.ProductionQueue.Orders.First(order =>
                GameplayCatalog.Canonicalize(order.UnitTypeId) == step.TypeId).OrderId;
            _requestSent = true;
            State = AIPlanExecutionState.InProgress;
            LastDecision = $"Waiting for already queued {step.TypeId}.";
            return;
        }
        if (!producer.CanProduceUnit(world, step.TypeId))
        {
            State = AIPlanExecutionState.WaitingForProducer;
            LastDecision = $"Waiting for {producer.GameplayTypeId} to accept {step.TypeId}.";
            return;
        }
        PurchaseQuote quote = Quote(PurchasableType.Unit, step.TypeId, producer.UnitId);
        if (!CanPay(quote))
        {
            WaitForResources(step, quote.FinalPrice);
            return;
        }
        _activeProducerId = producer.UnitId;
        _ = _commands.TrainUnitAsync(producer.UnitId, step.TypeId);
        MarkRequest($"Requested training of {step.TypeId} in {step.ProducerTypeId}.");
    }

    private void EnsureAcknowledgedBuildingHasWorker(AIProductionPlanStep step)
    {
        if (_requestedBuildingId is not Guid siteId ||
            world.Units.FindById(siteId) is not Building { IsCompleted: false } site)
            return;

        MobileUnit? builder = world.Units.GetArmyUnits(actor.ArmyId).OfType<MobileUnit>()
            .Where(unit => unit.ArmyId == actor.ArmyId && !unit.IsDying && !unit.IsEmbarked && !unit.IsLeavingBuilding &&
                unit.Occupancy?.IsOperational != false && unit.BuildRate > 0.0f && string.Equals(unit.GameplayTypeId,
                    step.ProducerTypeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(unit => world.AIOrderQueues.TryGetValue(actor.ArmyId, out var reservations) &&
                !reservations.CanUse(unit.UnitId, _requestedBuildingId))
            .ThenBy(unit => unit.UnitId)
            .FirstOrDefault();
        if (builder is null || builder.IsBuilding || builder.TargetBuildingId == siteId ||
            _retryElapsed < RetrySeconds)
            return;

        _ = _commands.ConstructAsync([builder.UnitId], site.UnitId);
        _retryElapsed = 0.0f;
        LastDecision = $"Reissued construction route to stalled builder for {step.TypeId}.";
    }

    private void ExecuteResearch(AIProductionPlanStep step)
    {
        Building? producer = FindProducer(step);
        if (producer is null)
        {
            State = AIPlanExecutionState.WaitingForProducer;
            LastDecision = $"Waiting for {step.ProducerTypeId} to research {step.TypeId}.";
            return;
        }
        _activeProducerId = producer.UnitId;
        if (AIStrategicCatalog.HasQueuedProduct(producer, step.TypeId))
        {
            _adoptedProductionOrderId = producer.ProductionQueue.Orders.First(order =>
                GameplayCatalog.Canonicalize(order.UnitTypeId) == step.TypeId).OrderId;
            _requestSent = true;
            State = AIPlanExecutionState.InProgress;
            LastDecision = $"Waiting for already queued research {step.TypeId}.";
            return;
        }
        PurchaseQuote quote = Quote(PurchasableType.Research, step.TypeId, producer.UnitId);
        if (!CanPay(quote))
        {
            WaitForResources(step, quote.FinalPrice);
            return;
        }
        _ = _commands.ResearchAsync(producer.UnitId, step.TypeId);
        MarkRequest($"Requested research {step.TypeId} in {step.ProducerTypeId}.");
    }

    private void ExecuteCrewAssignment(AIProductionPlanStep step)
    {
        if (step.UnitId is not Guid crewId || step.TargetUnitId is not Guid targetId ||
            world.Units.FindById(crewId) is not MobileUnit crew || crew.IsDying || crew.IsEmbarked ||
            world.Units.FindById(targetId) is not Unit target || target.IsDying)
        {
            Fail("Crew assignment references unavailable units.");
            return;
        }
        _ = _commands.EnterUnitAsync(crewId, targetId,
            step.OccupantRole ?? RTS.OccupantRole.Crew);
        MarkRequest($"Requested crew {crewId.ToString("N")[..8]} for {targetId.ToString("N")[..8]}.");
    }

    private float GetProgress(AIProductionPlanStep step)
    {
        if (step.Kind == AIProductionPlanStepKind.BuildBuilding && _requestedBuildingId is Guid siteId &&
            world.Units.FindById(siteId) is Building site) return site.ConstructionProgress;
        if (step.Kind is AIProductionPlanStepKind.TrainUnit or AIProductionPlanStepKind.Research)
        {
            Building? producer = FindProducer(step);
            // Progress of the FIFO head counts when our paid order is behind it.
            if (producer?.ProductionQueue.ActiveOrder is ProductionOrder order)
            {
                if (_observedProductionHead != order.OrderId)
                { _observedProductionHead = order.OrderId; _progress.Reset(); }
                return order.ElapsedSeconds;
            }
        }
        if (step.Kind == AIProductionPlanStepKind.AssignCrew && step.UnitId is Guid crewId &&
            step.TargetUnitId is Guid targetId && world.Units.FindById(crewId) is Unit crew &&
            world.Units.FindById(targetId) is Unit target)
            return -Vector2.Distance(new(crew.Position.X, crew.Position.Z), new(target.Position.X, target.Position.Z));
        return 0;
    }

    private bool IsStepAcknowledged(AIProductionPlanStep step) => step.Kind switch
    {
        AIProductionPlanStepKind.BuildBuilding =>
            _requestedBuildingId is Guid id && world.Units.FindById(id) is Building { IsDying: false },
        AIProductionPlanStepKind.TrainUnit or AIProductionPlanStepKind.Research =>
            FindProducer(step)?.ProductionQueue.Orders.Any(order =>
                order.OrderId == (_receipt?.Request.ProductionOrderId ?? _adoptedProductionOrderId)) == true,
        AIProductionPlanStepKind.AssignCrew =>
            step.UnitId is Guid crewId && world.Units.FindById(crewId) is MobileUnit crew &&
            crew.PendingEnterContainerId == step.TargetUnitId,
        _ => false
    };

    private bool IsStepComplete(AIProductionPlanStep step)
    {
        if (_receipt?.Result.ManagedByQueue == true)
            return _receipt.Result.Status == AIOrderStatus.Completed;
        if (_adoptedProductionOrderId is Guid adopted)
            return FindProducer(step)?.ProductionQueue.WasCompleted(adopted) == true;
        return step.Kind switch
        {
        AIProductionPlanStepKind.BuildBuilding =>
            _requestedBuildingId is Guid id && world.Units.FindById(id) is Building { IsCompleted: true, IsDying: false },
        AIProductionPlanStepKind.TrainUnit =>
            _receipt?.Request.ProductionOrderId is Guid productionId
                ? FindProducer(step)?.ProductionQueue.WasCompleted(productionId) == true
                : false,
        AIProductionPlanStepKind.Research =>
            GameplayCatalog.Find(PurchasableType.Research, step.TypeId)?.GrantedPerk is PerkType perk &&
            Globals.Game.Armies.Find(actor.ArmyId)?.Perks.Has(perk) == true,
        AIProductionPlanStepKind.AssignCrew =>
            step.UnitId is Guid crewId && step.TargetUnitId is Guid targetId &&
            world.Units.FindById(crewId) is Unit { IsEmbarked: true } crew &&
            crew.ContainerUnitId == targetId,
        _ => false
        };
    }

    private Building? FindProducer(AIProductionPlanStep step)
    {
        if (_activeProducerId is Guid id && world.Units.FindById(id) is Building active &&
            active.ArmyId == actor.ArmyId && !active.IsDying) return active;
        if (_activeProducerId is not null && _requestSent) return null;
        PurchasableType type = step.Kind == AIProductionPlanStepKind.Research ? PurchasableType.Research : PurchasableType.Unit;
        GameplayDefinition? product = GameplayCatalog.Find(type, step.TypeId);
        if (product is null) return null;
        return AIStrategicCatalog.FindAvailableProducer(world, actor.ArmyId, product);
    }

    private PurchaseQuote Quote(PurchasableType type, string typeId, Guid producerId) =>
        Globals.Game.Pricing.GetQuote(new PurchaseRequest(type, typeId, actor.ArmyId, producerId));

    private bool CanPay(PurchaseQuote quote) => AIResourcePlanner.CanPropose(world, actor.ArmyId, quote);

    private void WaitForResources(AIProductionPlanStep step, int cost)
    {
        State = AIPlanExecutionState.WaitingForResources;
        LastDecision = $"Waiting for {cost} resources plus reserve for {step.TypeId}.";
    }

    private void MarkRequest(string decision)
    {
        if (!_requestSent) _progress.Reset();
        _requestSent = true;
        _receipt = _commands.LastRequest;
        _retryElapsed = 0.0f;
        State = AIPlanExecutionState.WaitingForHost;
        LastDecision = decision;
    }

    private void AdvanceStep()
    {
        _stepIndex++;
        ResetStepState();
        if (_plan is null || _stepIndex >= _plan.Steps.Count)
        {
            State = AIPlanExecutionState.Completed;
            LastDecision = "Production plan completed.";
        }
        else
        {
            State = AIPlanExecutionState.InProgress;
            LastDecision = $"Advancing to plan step {_stepIndex + 1}/{_plan.Steps.Count}.";
        }
    }

    private void ResetStepState()
    {
        _retryElapsed = RetrySeconds;
        _requestedBuildingId = null;
        _requestSent = false;
        _adoptedProductionOrderId = null;
        _receipt = null;
        _activeProducerId = null;
        _wasAcknowledged = false;
        _observedProductionHead = null;
        _progress.Reset();
    }

    private void Fail(string message, AIOrderFailure failure = AIOrderFailure.Validation)
    {
        _receipt?.Result.Set(AIOrderStatus.Failed, failure, message);
        State = AIPlanExecutionState.Failed;
        LastDecision = message;
    }
}
