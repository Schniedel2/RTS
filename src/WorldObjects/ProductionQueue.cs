using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public sealed record ProductionOrderState(
    Guid OrderId,
    string UnitTypeId,
    Guid RequestedByPlayerId,
    float DurationSeconds,
    float ElapsedSeconds);

public sealed record ProductionQueueState(ProductionOrderState[] Orders);

public sealed class ProductionOrder
{
    public Guid OrderId { get; }
    public string UnitTypeId { get; }
    public Guid RequestedByPlayerId { get; }
    public float DurationSeconds { get; }
    public float ElapsedSeconds { get; internal set; }
    public float Progress => DurationSeconds <= 0.0f
        ? 1.0f
        : Math.Clamp(ElapsedSeconds / DurationSeconds, 0.0f, 1.0f);

    internal ProductionOrder(
        Guid orderId,
        string unitTypeId,
        Guid requestedByPlayerId,
        float durationSeconds,
        float elapsedSeconds = 0.0f)
    {
        OrderId = orderId;
        UnitTypeId = unitTypeId;
        RequestedByPlayerId = requestedByPlayerId;
        DurationSeconds = durationSeconds;
        ElapsedSeconds = elapsedSeconds;
    }

    internal ProductionOrderState GetState() => new(
        OrderId,
        UnitTypeId,
        RequestedByPlayerId,
        DurationSeconds,
        ElapsedSeconds);
}

/// <summary>Host-authoritative FIFO queue used by production buildings.</summary>
public sealed class ProductionQueue
{
    private readonly List<ProductionOrder> _orders = [];

    public int Capacity { get; set; } = 10;
    public IReadOnlyList<ProductionOrder> Orders => _orders;
    public ProductionOrder? ActiveOrder => _orders.Count > 0 ? _orders[0] : null;

    public bool Enqueue(
        Guid orderId,
        string unitTypeId,
        Guid requestedByPlayerId,
        float durationSeconds)
    {
        if (orderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(unitTypeId) ||
            durationSeconds <= 0.0f ||
            _orders.Count >= Capacity ||
            _orders.Any(order => order.OrderId == orderId))
        {
            return false;
        }

        _orders.Add(new ProductionOrder(
            orderId,
            unitTypeId,
            requestedByPlayerId,
            durationSeconds));
        return true;
    }

    public bool Update(float elapsedSeconds, out ProductionOrder? completedOrder)
    {
        completedOrder = null;
        if (elapsedSeconds <= 0.0f || _orders.Count == 0)
            return false;

        ProductionOrder activeOrder = _orders[0];
        activeOrder.ElapsedSeconds = Math.Min(
            activeOrder.DurationSeconds,
            activeOrder.ElapsedSeconds + elapsedSeconds);
        if (activeOrder.ElapsedSeconds < activeOrder.DurationSeconds)
            return false;

        _orders.RemoveAt(0);
        completedOrder = activeOrder;
        return true;
    }

    public ProductionQueueState GetState() => new(
        _orders.Select(order => order.GetState()).ToArray());

    public void ApplyState(ProductionQueueState? state)
    {
        _orders.Clear();
        if (state?.Orders is null)
            return;

        foreach (ProductionOrderState order in state.Orders.Take(Capacity))
        {
            if (order.OrderId == Guid.Empty ||
                string.IsNullOrWhiteSpace(order.UnitTypeId) ||
                order.DurationSeconds <= 0.0f)
            {
                continue;
            }

            _orders.Add(new ProductionOrder(
                order.OrderId,
                order.UnitTypeId,
                order.RequestedByPlayerId,
                order.DurationSeconds,
                Math.Clamp(order.ElapsedSeconds, 0.0f, order.DurationSeconds)));
        }
    }
}
