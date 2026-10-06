using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunOrderQueueChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            int price = Globals.Game.Pricing.GetQuote(new(PurchasableType.Unit, "engineer", scenario.Army.Id,
                scenario.Barracks.UnitId)).FinalPrice;
            scenario.Army.Resources = price;
            using (queue.Collect())
            {
                queue.Run(AIOrderPriority.Production, () => commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner"));
                queue.Run(AIOrderPriority.Defense, () => commands.TrainUnitAsync(scenario.Barracks.UnitId, "engineer"));
                Check(queue.Orders.All(order => !order.Sent), "AI proposals wait until all controllers have proposed");
            }
            Check(queue.Orders[1].Sent && !queue.Orders[0].Sent, "Higher-priority purchase wins producer arbitration");
            Check(queue.ReservedResources > 0, "In-flight purchase reserves funds until host acknowledgement");
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.ReservedResources == 0, "Host confirmation releases accounting reservation");
            Check(queue.Orders[0].State == AIQueuedOrderState.WaitingForProducer,
                "Lower priority purchase waits instead of competing for spent money");
            scenario.Army.Resources = 10000;
            scenario.Barracks.ProductionQueue.Update(100, out _); queue.Dispatch();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.Orders[0].State == AIQueuedOrderState.InProgress && queue.Orders[1].State == AIQueuedOrderState.Completed,
                "Deferred production resumes through real host and producer FIFO");
            Check(scenario.Barracks.ProductionQueue.Orders.Count == 1, "Arbitration does not lose accepted purchases");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            MobileUnit worker = scenario.World.Units.GetArmyUnits(scenario.Army.Id).OfType<MobileUnit>()
                .Single(unit => unit.BuildRate > 0);
            Building optional = scenario.AddBuilding("reaktor", new(30.5f, 0, 30.5f), false);
            Building urgent = scenario.AddBuilding("reaktor", new(40.5f, 0, 40.5f), false);
            queue.Run(AIOrderPriority.Expansion, () => commands.ConstructAsync([worker.UnitId], optional.UnitId));
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.Orders[0].State == AIQueuedOrderState.InProgress, "Accepted construction retains worker lease");
            queue.Run(AIOrderPriority.Power, () => commands.ConstructAsync([worker.UnitId], urgent.UnitId));
            Check(queue.Orders[0].State == AIQueuedOrderState.Paused, "Urgent power work pauses optional paid construction");
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(worker.TargetBuildingId == urgent.UnitId, "Preemption uses actual host construction command");
            using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
            {
                for (int i = 0; i < 90; i++)
                {
                    urgent.AdvanceConstruction(0.1f);
                    monitor.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
                }
                scenario.Network.Update(); scenario.PumpHost();
                Check(!scenario.Messages.Any(message => message.Type == NetworkMessageType.CancelConstructionRequest &&
                    message.UnitId == optional.UnitId), "Paused construction is excluded from stall cancellation");
            }
            commands.ConstructAsync([worker.UnitId], optional.UnitId).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost();
            Check(worker.TargetBuildingId == urgent.UnitId, "Paused controller retry cannot steal reserved worker");
            urgent.AdvanceConstruction(urgent.TotalBuildingPointsNeeded); queue.Dispatch();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(worker.TargetBuildingId == optional.UnitId, "Paid optional site resumes after urgent work finishes");
            Check(queue.Orders[0].State == AIQueuedOrderState.InProgress, "Resumed order returns to in-progress status");
            optional.AdvanceConstruction(optional.TotalBuildingPointsNeeded); queue.Dispatch();
            Check(queue.Orders.All(order => order.State == AIQueuedOrderState.Completed), "Completion releases both construction jobs");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            RequestReceipt pending = commands.LastRequest!;
            Check(queue.Orders[0].State == AIQueuedOrderState.WaitingForResources && pending.State == LocalRequestState.Pending,
                "Scheduler waiting is distinct from host rejection");
            queue.Update(61);
            Check(queue.Orders[0].State == AIQueuedOrderState.Failed && pending.State == LocalRequestState.Abandoned,
                "Unaffordable queued proposal has bounded waiting and allows controller replanning");
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            pending = commands.LastRequest!;
            queue.Dispose();
            Check(pending.State == LocalRequestState.Abandoned && queue.ReservedResources == 0,
                "Reset abandons deferred requests and clears reservations");
            scenario.Army.Resources = 10000;
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost();
            Check(commands.LastRequest!.State == LocalRequestState.Accepted, "Disposed queue no longer intercepts player gateway");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 10000;
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            using (queue.Collect())
            {
                commands.TrainUnitAsync(Guid.NewGuid(), "gunner").GetAwaiter().GetResult();
                commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            }
            Check(queue.Orders.All(order => order.Sent), "Independent producers dispatch without a global lock");
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.Orders[0].State == AIQueuedOrderState.Failed && queue.ReservedResources == 0,
                "Rejected host request releases funds and does not block unrelated purchases");
            Check(queue.Orders[1].State == AIQueuedOrderState.InProgress, "Valid request succeeds alongside rejected producer");
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "engineer").GetAwaiter().GetResult();
            RequestReceipt pending = commands.LastRequest!;
            scenario.Network.Disconnect(); queue.Dispatch();
            Check(pending.State == LocalRequestState.Abandoned && queue.ReservedResources == 0,
                "Session change discards old-generation queue and reservations");
        }
        return checks;
    }
}

