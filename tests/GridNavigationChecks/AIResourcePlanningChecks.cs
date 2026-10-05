using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private sealed class DiscountBarracks : Building, IPurchasePriceModifierProvider
    {
        public override string GameplayTypeId => "gdi-barracks";
        public DiscountBarracks(Vector3 position, Guid id) : base(position, id)
        { Width = Length = 1; ApplyCatalogMetadata(); }
        public int DiscountBasisPoints;
        public IEnumerable<PriceModifier> GetPriceModifiers(PurchaseRequest request) =>
            [new("test producer upgrade", DiscountBasisPoints)];
    }

    private static int RunResourcePlanningChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        foreach (AIOrderPriority priority in Enum.GetValues<AIOrderPriority>())
            Check(AIResourcePlanner.ReserveFor(priority) == (priority >= AIOrderPriority.Defense ? 0 : 800),
                $"Shared reserve policy for {priority}");

        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            int crewPrice = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, "engineer", scenario.Army.Id,
                scenario.Barracks.UnitId)).FinalPrice;
            int gunnerPrice = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, "gunner", scenario.Army.Id,
                scenario.Barracks.UnitId)).FinalPrice;
            scenario.Army.Resources = crewPrice - 1;
            using (queue.Collect())
            {
                queue.Run(AIOrderPriority.Defense, () => commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner"));
                queue.Run(AIOrderPriority.Power, () => commands.TrainUnitAsync(scenario.Barracks.UnitId, "engineer"));
            }
            Check(queue.Orders.All(order => !order.Sent), "Unaffordable power need prevents cheaper production spending");
            Check(queue.Budget.ProtectedResources == scenario.Army.Resources && queue.ReservedResources == 0,
                "Saving for planned expenses differs from in-flight purchase reservation");
            Check(queue.Budget.PlannedExpenses == crewPrice + gunnerPrice, "Shared budget includes both controllers' wishes");
            for (int i = 0; i < 5; i++) queue.Dispatch();
            Check(queue.Orders.All(order => !order.Sent) && queue.Budget.PlannedExpenses == crewPrice + gunnerPrice,
                "Repeated planning neither duplicates nor accumulates reserved expenses");
            scenario.Army.Resources = crewPrice; queue.Dispatch();
            Check(queue.Orders[1].Sent && !queue.Orders[0].Sent, "Urgent power crew can spend below normal safety reserve");
            Check(queue.Budget.InFlightExpenses == crewPrice && queue.ReservedResources == crewPrice,
                "Budget and queue agree on the exact current purchase reservation");
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(scenario.Army.Resources == 0 && queue.Budget.InFlightExpenses == 0,
                "Host purchase deducted once and releases the temporary accounting reservation");
            Check(queue.Budget.RunningProductionOrders == 1, "Budget observes already-paid production FIFO");
            scenario.Army.Resources = gunnerPrice + AIResourcePlanner.SafetyReserve;
            scenario.Barracks.ProductionQueue.Update(100, out _); queue.Dispatch();
            Check(queue.Orders[0].Sent, "Paid power production is not charged again when financing the next order");
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(scenario.Army.Resources == AIResourcePlanner.SafetyReserve && queue.Budget.RunningProductionOrders == 1,
                "Ordinary production retains the single shared reserve and recognizes the remaining paid order");
            Check(queue.Budget.PlannedExpenses == 0 && queue.Budget.InFlightExpenses == 0,
                "Accepted production no longer appears as unpaid planned expense");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            scenario.Army.Resources = 10000;
            queue.Budget.MinimumPurchasePriority = AIOrderPriority.Survival;
            queue.Run(AIOrderPriority.Research, () => commands.TrainUnitAsync(scenario.Barracks.UnitId, "engineer"));
            Check(!queue.Orders[0].Sent, "Reconstruction pauses old discretionary proposals even after income arrives");
            queue.Budget.MinimumPurchasePriority = AIOrderPriority.Expansion; queue.Dispatch();
            Check(queue.Orders[0].Sent, "Production becomes eligible again after core recovery");
            queue.Dispose();
            Check(queue.Budget.InFlightExpenses == 0 && queue.Budget.ProtectedResources == 0 &&
                queue.Budget.PlannedExpenses == 0, "Queue reset clears shared budget accounting");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 0;
            var quote = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, "gunner", scenario.Army.Id,
                scenario.Barracks.UnitId));
            Check(!AIResourcePlanner.CanPropose(scenario.World, scenario.Army.Id, quote),
                "Standalone controller uses the same budget policy without bypassing affordability");
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            Check(AIResourcePlanner.CanPropose(scenario.World, scenario.Army.Id, quote),
                "Queued controllers may propose a valid unaffordable wish for global savings");
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            executor.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            Check(queue.Orders.Count == 1 && queue.Orders[0].State == AIQueuedOrderState.WaitingForResources,
                "Production planner actually delegates its resource decision to the army queue");
            for (int i = 0; i < 5; i++) executor.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(4)));
            Check(queue.Orders.Count == 1, "Waiting controller does not generate repeated competing purchase requests");
            scenario.Army.Resources = quote.FinalPrice + AIResourcePlanner.SafetyReserve; queue.Dispatch();
            scenario.Network.Update(); scenario.PumpHost(); executor.Update(new GameTime());
            Check(executor.State == AIPlanExecutionState.InProgress && scenario.Barracks.ProductionQueue.Orders.Count == 1,
                "Income resumes deferred request through real host confirmation and existing controller");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            int price = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, "gunner", scenario.Army.Id,
                scenario.Barracks.UnitId)).FinalPrice;
            scenario.Army.Resources = price + AIResourcePlanner.SafetyReserve - 1;
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            Check(!queue.Orders[0].Sent, "Normal production enforces the shared reserve at its exact boundary");
            scenario.Army.Resources++; queue.Dispatch();
            Check(queue.Orders[0].Sent && queue.Budget.AvailableResources == AIResourcePlanner.SafetyReserve,
                "One added resource makes the centrally budgeted production affordable");
            scenario.Army.Resources = 0;
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.Orders[0].State == AIQueuedOrderState.Failed && queue.Budget.InFlightExpenses == 0,
                "Host rejection after an external expense releases the shared budget");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Remove(scenario.Barracks);
            var producer = new DiscountBarracks(new(5.5f, 0, 10.5f), scenario.NextId());
            producer.AdvanceConstruction(producer.TotalBuildingPointsNeeded);
            scenario.Add(producer);
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            int fullPrice = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, "gunner", scenario.Army.Id,
                producer.UnitId)).FinalPrice;
            scenario.Army.Resources = AIResourcePlanner.SafetyReserve + fullPrice / 2;
            commands.TrainUnitAsync(producer.UnitId, "gunner").GetAwaiter().GetResult();
            Check(!queue.Orders[0].Sent, "Undiscounted proposal waits on shared budget");
            producer.DiscountBasisPoints = -5000; queue.Dispatch();
            Check(queue.Orders[0].Sent && queue.ReservedResources == fullPrice / 2,
                "Deferred order re-quotes a new producer discount instead of caching the old price");
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(scenario.Army.Resources == AIResourcePlanner.SafetyReserve && queue.Budget.RunningProductionOrders == 1,
                "Real host agrees with the centralized discounted reservation");
        }
        return checks;
    }
}
