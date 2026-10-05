using System;
using System.Linq;
using RTS;
using RTS.Network;
using Microsoft.Xna.Framework;

internal static partial class AIReconstructionChecks
{
    private static int RunReservationChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 10000;
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.ReservationFor(scenario.Barracks.UnitId)?.State == AIQueuedOrderState.InProgress,
                "Host acceptance retains production lease until its exact FIFO order finishes");
            Building other = scenario.AddBuilding("gdi-barracks", new(40.5f, 0, 10.5f));
            Check(AIStrategicCatalog.FindAvailableProducer(scenario.World, scenario.Army.Id,
                GameplayCatalog.Find(PurchasableType.Unit, "gunner")!) == other,
                "Catalog prefers a free compatible producer over another controller's reserved one");
            queue.Run(AIOrderPriority.Survival, () => commands.TrainUnitAsync(scenario.Barracks.UnitId, "engineer"), urgent: true);
            Check(queue.Orders[1].State == AIQueuedOrderState.WaitingForProducer && !queue.Orders[1].Sent,
                "Urgency cannot cancel or steal an already-paid production FIFO");
            scenario.Barracks.ProductionQueue.Update(100, out _); queue.Dispatch();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.Orders[0].State == AIQueuedOrderState.Completed && queue.Orders[1].State == AIQueuedOrderState.InProgress,
                "FIFO completion releases producer and dispatches its waiting order exactly once");
            scenario.Remove(scenario.Barracks); queue.Dispatch();
            Check(queue.Orders[1].State == AIQueuedOrderState.Failed && queue.ReservationFor(scenario.Barracks.UnitId) is null,
                "Producer destruction releases its outstanding reservation");
            commands.ResearchAsync(scenario.Home.UnitId, ResearchProjects.AirTechnologyId).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.ReservationFor(scenario.Home.UnitId)?.State == AIQueuedOrderState.InProgress,
                "Research reserves its base producer for the actual research order");
            scenario.Home.ProductionQueue.Update(100, out _); queue.Dispatch();
            Check(queue.ReservationFor(scenario.Home.UnitId) is null, "Research FIFO completion frees its producer");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            MobileUnit worker = scenario.World.Units.GetArmyUnits(scenario.Army.Id).OfType<MobileUnit>()
                .Single(unit => unit.BuildRate > 0);
            Building first = scenario.AddBuilding("reaktor", new(30.5f, 0, 30.5f), false);
            Building routine = scenario.AddBuilding("reaktor", new(40.5f, 0, 40.5f), false);
            Building emergency = scenario.AddBuilding("reaktor", new(45.5f, 0, 45.5f), false);
            queue.Run(AIOrderPriority.Expansion, () => commands.ConstructAsync([worker.UnitId], first.UnitId));
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            queue.Run(AIOrderPriority.Defense, () => commands.ConstructAsync([worker.UnitId], routine.UnitId));
            Check(queue.Orders[1].State == AIQueuedOrderState.WaitingForWorker && !queue.IsPaused(first.UnitId),
                "Routine defensive expansion cannot steal a worker from ongoing construction");
            for (int i = 0; i < 5; i++)
            {
                commands.ConstructAsync([worker.UnitId], routine.UnitId).GetAwaiter().GetResult();
                queue.Dispatch(); scenario.Network.Update(); scenario.PumpHost();
            }
            Check(worker.TargetBuildingId == first.UnitId, "Repeated controller retries do not alternate a bulldozer between sites");
            MobileUnit replacement = scenario.Add(new Builder(new(3.5f, 0, 7.5f), scenario.NextId()));
            Check(AIStrategicCatalog.FindBuilder(scenario.World, scenario.Army.Id, "reaktor") == replacement,
                "Builder selection prefers the unreserved worker");
            queue.Run(AIOrderPriority.Defense, () => commands.ConstructAsync([worker.UnitId], emergency.UnitId), urgent: true);
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.IsPaused(first.UnitId) && worker.TargetBuildingId == emergency.UnitId,
                "Acute defense alarm may pause a lower-priority worker assignment");
            emergency.AdvanceConstruction(emergency.TotalBuildingPointsNeeded); queue.Dispatch();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            // Pending routine defense now has higher priority than the paused expansion.
            Check(worker.TargetBuildingId == routine.UnitId, "Worker is awarded to the highest waiting compatible job after alarm");
            routine.AdvanceConstruction(routine.TotalBuildingPointsNeeded); queue.Dispatch();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(worker.TargetBuildingId == first.UnitId, "Paid original site resumes after higher-priority jobs finish");
            scenario.Remove(worker);
            commands.ConstructAsync([replacement.UnitId], first.UnitId).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.ReservationFor(replacement.UnitId)?.SiteId == first.UnitId &&
                replacement.TargetBuildingId == first.UnitId && queue.ReservationFor(worker.UnitId) is null,
                "A valid replacement worker takes over the existing paid site without stale ownership");
            queue.Dispose();
            Check(queue.ReservationFor(replacement.UnitId) is null, "Reset releases accepted worker reservations");
        }
        foreach (var product in new[] { (Producer: "vehicle-factory", Unit: "tank"), (Producer: "helipad", Unit: "helicopter") })
        {
            using var scenario = new Scenario(false);
            scenario.Army.Resources = 10000;
            scenario.Army.Perks.GrantPermanent(PerkType.AirTechnology, scenario.NextId());
            Building producer = scenario.AddBuilding(product.Producer, new(40.5f, 0, 10.5f));
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(producer.UnitId, product.Unit).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(queue.ReservationFor(producer.UnitId)?.State == AIQueuedOrderState.InProgress,
                $"Catalog producer {product.Producer} is reserved for its accepted {product.Unit} order");
            producer.ProductionQueue.Update(0.1f, out _); queue.Dispatch();
            Check(!queue.CanUse(producer.UnitId), $"Partial progress does not release {product.Producer}");
            producer.ProductionQueue.Update(100, out _); queue.Dispatch();
            Check(queue.ReservationFor(producer.UnitId) is null, $"Completion releases {product.Producer}");
        }
        return checks;
    }
}
