using System;
using System.Collections.Generic;
using System.Linq;
using RTS.Network;

namespace RTS;

public enum AIOrderPriority { Expansion, Research, Production, Defense, Economy, Power, Survival }
public enum AIQueuedOrderState { Queued, WaitingForResources, WaitingForWorker, WaitingForProducer, Requested, InProgress, Paused, Completed, Failed }

/// <summary>Controller-local arbitration. Execution always uses the ordinary host request path.</summary>
public sealed class AIOrderQueue : IDisposable
{
    public sealed class Order(NetworkMessage request, RequestReceipt receipt, AIOrderPriority priority, bool urgent)
    {
        public NetworkMessage Request { get; } = request;
        public RequestReceipt Receipt { get; } = receipt;
        public AIOrderPriority Priority { get; internal set; } = priority;
        public bool Urgent { get; internal set; } = urgent;
        public AIOrderResult Result => Receipt.Result;
        private AIQueuedOrderState _state;
        public AIQueuedOrderState State
        {
            get => _state;
            internal set
            {
                _state = value;
                if (value == AIQueuedOrderState.InProgress) Result.Set(AIOrderStatus.InProgress);
                else if (value == AIQueuedOrderState.Completed) Result.Set(AIOrderStatus.Completed);
                else if (value == AIQueuedOrderState.Failed) Result.Set(AIOrderStatus.Failed,
                    AIOrderFailure.InvalidTarget, "The assigned worker, producer or construction target is unavailable.");
            }
        }
        public int ReservedResources { get; internal set; }
        internal float WaitingSeconds;
        internal bool Sent;
        internal bool ResumePending;
        internal RequestReceipt? ExecutionReceipt;
        internal Guid? SiteId => Request.Type == NetworkMessageType.BuildRequest ? Request.UnitId : Request.ConstructionSiteId;
        public IReadOnlyList<Guid> Workers { get; internal set; } = request.UnitIds ?? [];
        public Guid? ProducerId => Request.Type is NetworkMessageType.TrainUnitRequest or NetworkMessageType.ResearchRequest
            ? Request.UnitId : null;
    }

    private readonly GameWorld _world;
    private readonly Player _actor;
    private readonly NetworkHandler _network;
    private readonly List<Order> _orders = [];
    private AIOrderPriority _priority = AIOrderPriority.Production;
    private bool _collecting;
    private bool _urgent;
    private bool _disposed;
    private readonly long _generation;
    public AIResourcePlanner Budget { get; } = new();
    public IReadOnlyList<Order> Orders => _orders;
    public int ReservedResources => _orders.Sum(order => order.ReservedResources);
    public string Diagnostic => $"AI queue: {_orders.Count(order => !Terminal(order))} active, " +
        $"{ReservedResources} in flight, {Budget.ProtectedResources} saved, " +
        $"{Budget.PlannedExpenses} planned, {Budget.RunningProductionOrders} producing";

    public AIOrderQueue(GameWorld world, Player actor, NetworkHandler network)
    {
        _world = world; _actor = actor; _network = network; _generation = network.SessionGeneration;
        world.AIOrderQueues[actor.ArmyId] = this;
        network.SetLocalAIOrderRouter(actor.Id, Submit);
    }

    public void Update(float elapsedSeconds)
    {
        foreach (Order order in _orders)
        {
            if (order.Sent || Terminal(order)) continue;
            order.WaitingSeconds += Math.Max(0, elapsedSeconds);
            if (order.WaitingSeconds < AIOrderProgressMonitor.ProductionTimeoutSeconds) continue;
            order.Result.Set(AIOrderStatus.Failed, AIOrderFailure.Timeout, "AI scheduling timed out.");
            _network.AbandonLocalRequest(order.Request, "AI scheduling timed out while waiting for resources or workers.");
            order.State = AIQueuedOrderState.Failed;
            order.ReservedResources = 0;
        }
    }

    private static bool CanPreempt(AIOrderPriority priority, bool urgent, AIOrderPriority ownerPriority) =>
        priority > ownerPriority && (priority >= AIOrderPriority.Power || priority == AIOrderPriority.Defense && urgent);

    public Order? ReservationFor(Guid unitId) => _orders.FirstOrDefault(order => !Terminal(order) &&
        order.Sent && order.State != AIQueuedOrderState.Paused &&
        (order.ProducerId == unitId || order.Workers.Contains(unitId)));

    public bool CanUse(Guid unitId, Guid? siteId = null)
    {
        Order? owner = ReservationFor(unitId);
        return owner is null || siteId.HasValue && owner.SiteId == siteId ||
            owner.ProducerId is null && owner.Receipt.State == LocalRequestState.Accepted &&
            CanPreempt(_priority, _urgent, owner.Priority);
    }

    public bool IsPaused(Guid siteId) => _orders.Any(order => order.SiteId == siteId && order.State == AIQueuedOrderState.Paused);

    public IDisposable Collect()
    {
        Dispatch(dispatchOrders: false);
        _collecting = true;
        return new Scope(() => { _collecting = false; Dispatch(); });
    }

    public void Run(AIOrderPriority priority, Action action, bool urgent = false)
    {
        AIOrderPriority previous = _priority;
        bool previousUrgency = _urgent;
        _priority = priority;
        _urgent = urgent;
        try { action(); } finally { _priority = previous; _urgent = previousUrgency; }
    }

    private bool Submit(NetworkMessage request)
    {
        if (_disposed) return false;
        if (request.Type is not (NetworkMessageType.BuildRequest or NetworkMessageType.BuildConstructionRequest
            or NetworkMessageType.TrainUnitRequest or NetworkMessageType.ResearchRequest)) return false;
        // A real replacement site supersedes an unpaid proposal for that same need.
        // Keep the existing site and its refund/identity, rather than buying it twice.
        if (request.Type == NetworkMessageType.BuildConstructionRequest && request.ConstructionSiteId is Guid target &&
            _world.Units.FindById(target) is Building replacement)
            foreach (Order obsolete in _orders.Where(order => !order.Sent && !Terminal(order) &&
                order.Request.Type == NetworkMessageType.BuildRequest && order.Priority <= _priority &&
                order.Request.UnitTypeId == replacement.GameplayTypeId &&
                order.Workers.Intersect(request.UnitIds ?? []).Any()))
            {
                _network.AbandonLocalRequest(obsolete.Request, "Existing construction supersedes unpaid building proposal.");
                obsolete.State = AIQueuedOrderState.Failed;
            }
        // Construction retries adopt the original site rather than competing with its owner.
        Order? existing = _orders.FirstOrDefault(order => !Terminal(order) &&
            (ReferenceEquals(order.Request, request) || request.Type == NetworkMessageType.BuildConstructionRequest &&
                order.SiteId == request.ConstructionSiteId));
        if (existing is not null)
        {
            if (!existing.Sent && _priority >= existing.Priority && (_urgent || _priority >= AIOrderPriority.Power))
            { existing.Priority = _priority; existing.Urgent |= _urgent; }

            if (request.Type == NetworkMessageType.BuildConstructionRequest)
            {
                _network.TrackRequest(request);
                if (existing.Sent && existing.State != AIQueuedOrderState.Paused &&
                    (request.UnitIds ?? []).Length > 0 && (request.UnitIds ?? []).All(id =>
                        CanUse(id, existing.SiteId) && _world.Units.FindById(id) is MobileUnit { IsDying: false } worker &&
                        worker.ArmyId == _actor.ArmyId && worker.BuildRate > 0))
                {
                    existing.Workers = request.UnitIds!;
                    existing.ExecutionReceipt = _network.TrackRequest(request);
                    _ = _network.SendToHostAsync(request);
                }
                else _network.AbandonLocalRequest(request, "Construction retry is deferred to its scheduled worker.");
            }
            if (!_collecting) Dispatch();
            return true;
        }
        var tracked = _network.TrackRequest(request)!;
        AIOrderPriority priority = request.Type == NetworkMessageType.ResearchRequest && _priority < AIOrderPriority.Power
            ? AIOrderPriority.Research : _priority;
        GameplayDefinition? product = GameplayCatalog.Find(request.Type == NetworkMessageType.BuildRequest
            ? PurchasableType.Building : PurchasableType.Unit, request.UnitTypeId);
        if (priority < AIOrderPriority.Economy && (product?.Building?.ResourceCapacity > 0 ||
            product?.AI?.Roles.HasFlag(AIUnitRole.Harvester) == true)) priority = AIOrderPriority.Economy;
        tracked.Result.ManagedByQueue = true;
        _orders.Add(new Order(request, tracked, priority, _urgent));
        if (!_collecting) Dispatch();
        return true;
    }

    private static bool Terminal(Order order) => order.State is AIQueuedOrderState.Completed or AIQueuedOrderState.Failed;

    public void Dispatch(bool dispatchOrders = true)
    {
        if (_disposed) return;
        if (!_network.CanRunAI(_actor.ArmyId) || _network.SessionGeneration != _generation) { Dispose(); return; }
        foreach (Order order in _orders)
        {
            if (Terminal(order)) continue;
            if (order.Result.Status == AIOrderStatus.Completed) { order.State = AIQueuedOrderState.Completed; order.ReservedResources = 0; continue; }
            if (order.Result.Status is AIOrderStatus.Rejected or AIOrderStatus.Failed)
            { order.State = AIQueuedOrderState.Failed; order.ReservedResources = 0; continue; }
            if (order.Receipt.State is LocalRequestState.Rejected or LocalRequestState.Abandoned)
            { order.State = AIQueuedOrderState.Failed; order.ReservedResources = 0; continue; }
            if (!order.Sent || order.Receipt.State == LocalRequestState.Pending) continue;
            if (order.ExecutionReceipt?.State is LocalRequestState.Rejected or LocalRequestState.Abandoned)
            {
                order.Result.Set(AIOrderStatus.Failed, order.ExecutionReceipt.Result.Failure, order.ExecutionReceipt.Reason);
                order.State = AIQueuedOrderState.Failed;
                order.ReservedResources = 0;
                continue;
            }
            order.ResumePending = order.ExecutionReceipt?.State == LocalRequestState.Pending;
            if (!_network.IsHost)
            {
                order.ReservedResources = 0;
                if (order.State != AIQueuedOrderState.Paused) order.State = AIQueuedOrderState.InProgress;
                continue;
            }
            order.ReservedResources = 0; // The host has now deducted the actual purchase price.
            if (order.SiteId is Guid siteId)
            {
                if (_world.Units.FindById(siteId) is not Building site || site.IsDying)
                    order.State = AIQueuedOrderState.Failed;
                else if (site.IsCompleted) order.State = AIQueuedOrderState.Completed;
                else if (order.State != AIQueuedOrderState.Paused) order.State = AIQueuedOrderState.InProgress;
            }
            else if (order.ProducerId is Guid producerId)
            {
                if (_world.Units.FindById(producerId) is not Building { IsDying: false } producer ||
                    producer.ArmyId != _actor.ArmyId)
                {
                    order.Result.Set(AIOrderStatus.Failed, AIOrderFailure.Producer, "The production building was lost.");
                    order.State = AIQueuedOrderState.Failed;
                }
                else if (producer.ProductionQueue.Orders.Any(item => item.OrderId == order.Request.ProductionOrderId))
                    order.State = AIQueuedOrderState.InProgress;
                else if (producer.ProductionQueue.WasCompleted(order.Request.ProductionOrderId!.Value))
                    order.State = AIQueuedOrderState.Completed;
                else
                {
                    order.Result.Set(AIOrderStatus.Failed, AIOrderFailure.Cancelled, "Confirmed production disappeared without completion.");
                    order.State = AIQueuedOrderState.Failed;
                }
            }
            else order.State = AIQueuedOrderState.Completed;
        }
        foreach (Order order in _orders.Where(order => order.Receipt.State == LocalRequestState.Accepted))
            _network.ReportRequestExecution(order.Receipt, order.Result.Status, order.Result.Failure, order.Result.Reason);
        Budget.Begin(_world.SimulationArmies.Find(_actor.ArmyId)?.Resources ?? 0, ReservedResources,
            _world.Units.GetArmyUnits(_actor.ArmyId).OfType<Building>()
                .Where(building => !building.IsDying).Sum(building => building.ProductionQueue.Orders.Count));
        if (!dispatchOrders) return;
        foreach (Order order in _orders.Where(order => !Terminal(order))
            .OrderByDescending(order => order.Priority).ToArray())
        {
            if (order.Sent && order.State != AIQueuedOrderState.Paused) continue;
            if (!order.Sent && !_network.AllowLocalAIRequest(order.Request))
            {
                _network.ResolveLocalRequest(order.Request, false, "AI recovery cooldown prevents this order.", AIOrderFailure.RecoveryCooldown);
                order.State = AIQueuedOrderState.Failed; continue;
            }
            if (!order.Sent && order.Request.Type != NetworkMessageType.BuildConstructionRequest &&
                order.Priority < Budget.MinimumPurchasePriority) continue;
            int price = 0;
            if (!order.Sent && order.Request.Type != NetworkMessageType.BuildConstructionRequest)
            {
                PurchasableType type = order.Request.Type == NetworkMessageType.BuildRequest ? PurchasableType.Building
                    : order.Request.Type == NetworkMessageType.ResearchRequest ? PurchasableType.Research : PurchasableType.Unit;
                PurchaseQuote quote = _world.SimulationPricing.GetQuote(new PurchaseRequest(type,
                    order.Request.UnitTypeId!, _actor.ArmyId, order.ProducerId ?? order.Workers.FirstOrDefault()));
                if (!quote.IsAvailable)
                {
                    _network.ResolveLocalRequest(order.Request, false, "Purchase is no longer available.",
                        quote.MissingPerks.Count > 0 ? AIOrderFailure.Perk : AIOrderFailure.Validation);
                    order.State = AIQueuedOrderState.Failed; continue;
                }
                price = quote.FinalPrice;
                Budget.PlannedExpenses += price;
            }
            if (order.ProducerId is Guid producer && _orders.Any(other => other != order && !Terminal(other) &&
                other.Sent && other.ProducerId == producer))
            { Budget.Protect(price); order.State = AIQueuedOrderState.WaitingForProducer; continue; }
            Order[] conflicts = _orders.Where(other => other != order && !Terminal(other) && other.Sent &&
                other.State != AIQueuedOrderState.Paused && order.Workers.Intersect(other.Workers).Any()).ToArray();
            if (conflicts.Any(other => !CanPreempt(order.Priority, order.Urgent, other.Priority) || other.Receipt.State == LocalRequestState.Pending || other.ExecutionReceipt?.State == LocalRequestState.Pending))
            { Budget.Protect(price); order.State = order.Sent ? AIQueuedOrderState.Paused : AIQueuedOrderState.WaitingForWorker; continue; }
            if (order.Workers.Any(id => _world.Units.FindById(id) is not MobileUnit { IsDying: false } worker ||
                worker.ArmyId != _actor.ArmyId))
            {
                if (!order.Sent) _network.AbandonLocalRequest(order.Request, "Required worker is unavailable.");
                order.State = AIQueuedOrderState.Failed; continue;
            }
            if (!Budget.TryAllocate(price, order.Priority))
            { order.State = AIQueuedOrderState.WaitingForResources; continue; }
            foreach (Order conflict in conflicts)
            { conflict.State = AIQueuedOrderState.Paused; conflict.ResumePending = false; }
            if (order.Sent)
            {
                if (order.ResumePending) continue;
                NetworkMessage resume = NetworkCommands.CreateBuildConstructionRequest(_actor.Id,
                    order.Workers.ToArray(), order.SiteId!.Value) with
                {
                    AIControllerArmyId = order.Request.AIControllerArmyId,
                    AIControllerActorId = order.Request.AIControllerActorId,
                    AIControllerGeneration = order.Request.AIControllerGeneration,
                    ControllerPeerId = order.Request.ControllerPeerId
                };
                order.ExecutionReceipt = _network.TrackRequest(resume);
                _ = _network.SendToHostAsync(resume);
                order.ResumePending = true;
                order.State = AIQueuedOrderState.InProgress;
            }
            else
            {
                order.ReservedResources = price;
                order.Sent = true;
                order.State = AIQueuedOrderState.Requested;
                _ = _network.SendToHostAsync(order.Request);
            }
        }
        // Bounded history; outstanding jobs remain until acknowledged or explicitly abandoned.
        while (_orders.Count > 128)
        {
            int index = _orders.FindIndex(Terminal);
            if (index < 0) break;
            _orders.RemoveAt(index);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_world.AIOrderQueues.GetValueOrDefault(_actor.ArmyId) == this)
        {
            _world.AIOrderQueues.Remove(_actor.ArmyId);
            _network.SetLocalAIOrderRouter(_actor.Id, null);
        }
        foreach (Order order in _orders)
        {
            if (!Terminal(order) && order.Receipt.State == LocalRequestState.Pending)
                _network.AbandonLocalRequest(order.Request, "AI order queue reset.");
            order.ReservedResources = 0;
            if (!Terminal(order))
            { order.Result.Set(AIOrderStatus.Failed, AIOrderFailure.Cancelled, "AI order queue reset."); order.State = AIQueuedOrderState.Failed; }
        }
        Budget.Begin(0, 0, 0);
    }

    private sealed class Scope(Action finish) : IDisposable
    {
        public void Dispose() => finish();
    }
}
