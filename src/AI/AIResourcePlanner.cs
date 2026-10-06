using System;

namespace RTS;

/// <summary>One budget policy for AI proposals and the army's economic order queue.</summary>
public sealed class AIResourcePlanner
{
    public const int SafetyReserve = 800;
    public int ConfiguredReserve { get; set; } = SafetyReserve;
    public AIOrderPriority MinimumPurchasePriority { get; internal set; } = AIOrderPriority.Expansion;
    public int Resources { get; private set; }
    public int InFlightExpenses { get; private set; }
    public int PlannedExpenses { get; internal set; }
    public int ProtectedResources { get; private set; }
    public int RunningProductionOrders { get; private set; }
    public int AvailableResources => Math.Max(0, Resources - InFlightExpenses - ProtectedResources);

    // Existential repairs, power, harvesters and defense may spend the safety reserve.
    public static int ReserveFor(AIOrderPriority priority) =>
        priority >= AIOrderPriority.Defense ? 0 : SafetyReserve;

    internal void Begin(int resources, int inFlightExpenses, int runningProductionOrders)
    {
        Resources = Math.Max(0, resources);
        InFlightExpenses = Math.Max(0, inFlightExpenses);
        RunningProductionOrders = runningProductionOrders;
        ProtectedResources = 0;
        PlannedExpenses = 0;
    }

    internal void Protect(int price) =>
        ProtectedResources += Math.Min(Math.Max(0, price), AvailableResources);

    // Called in descending priority/FIFO order. A deferred expensive order protects
    // its savings so cheaper subsequent purchases cannot continually starve it.
    internal bool TryAllocate(int price, AIOrderPriority priority)
    {
        if (price == 0) return true;
        if ((long)AvailableResources < (long)price + (priority >= AIOrderPriority.Defense ? 0 : ConfiguredReserve))
        { Protect(price); return false; }
        InFlightExpenses += price;
        return true;
    }

    public static bool CanPropose(GameWorld world, Guid armyId, PurchaseQuote quote,
        AIOrderPriority priority = AIOrderPriority.Production)
    {
        if (!quote.IsAvailable) return false;
        // The central queue must see affordable AND unaffordable strategic wishes.
        // Submission itself neither spends nor reserves resources.
        if (world.AIOrderQueues.ContainsKey(armyId)) return true;
        return (long)(world.SimulationArmies.Find(armyId)?.Resources ?? 0) >=
            (long)quote.FinalPrice + ReserveFor(priority);
    }
}
