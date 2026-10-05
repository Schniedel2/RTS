using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunOrderProgressChecks()
    {
        int checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new Exception(label); checks++; }
        GameTime Tick(float dt = 1) => new(TimeSpan.Zero, TimeSpan.FromSeconds(dt));

        var watch = new AIProgressWatch();
        Check(watch.Update("A", 0, 1, 3), "Progress watch begins a new objective");
        Check(watch.Update("A", 0, 2, 3) && !watch.Update("A", 0, 1, 3),
            "Unchanged objective times out despite refreshed commands");
        Check(watch.Update("A", 0.1f, 1, 3) && watch.SecondsWithoutProgress == 0,
            "Cumulative meaningful progress resets the deadline");
        Check(watch.MadeProgress, "Only genuine cumulative gains mark progress");
        watch.RestartDeadline();
        Check(watch.Update("A", 0, 1, 3) && !watch.MadeProgress,
            "A recovery deadline retains the best progress and rejects backward-forward oscillation");
        Check(watch.Update("B", -5, 10, 3), "A changed objective receives its own deadline");
        Check(!watch.Update("B", float.NaN, 1, 3), "Invalid progress cannot conceal a stalled order");

        using (var scenario = new Scenario(false))
        {
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(Guid.NewGuid(), "gepard").GetAwaiter().GetResult();
            LocalRequestReceipt receipt = commands.LastRequest!;
            Check(receipt.State == LocalRequestState.Pending, "Sending is distinct from host acceptance");
            scenario.Network.Update(); scenario.PumpHost();
            Check(receipt.State == LocalRequestState.Rejected && receipt.Reason is not null,
                "Real host rejection reports unavailable producer without waiting for timeout");
            scenario.Army.Resources = 10000;
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            receipt = commands.LastRequest!;
            scenario.Network.Update(); scenario.PumpHost();
            Check(receipt.State == LocalRequestState.Accepted && scenario.Barracks.ProductionQueue.Orders.Count == 1,
                "Actual accepted production resolves its local receipt after host application");
            commands.GotoAsync([scenario.Soldiers[0].UnitId], new(40.5f, 0, 40.5f)).GetAwaiter().GetResult();
            receipt = commands.LastRequest!;
            scenario.Network.Update(); scenario.Host.Update(new GameTime());
            Check(receipt.State == LocalRequestState.Pending,
                "Sliced route planning keeps the host receipt pending until a route is published");
            scenario.PumpHost();
            Check(receipt.State == LocalRequestState.Accepted,
                "Asynchronous route publication resolves the original request despite record copies");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 10000;
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            executor.Update(Tick()); scenario.Network.Update();
            for (int step = 0; step < 40; step++) executor.Update(Tick());
            Check(executor.State == AIPlanExecutionState.WaitingForHost &&
                scenario.Messages.Count(m => m.Type == NetworkMessageType.TrainUnitRequest) == 1,
                "Host backlog does not generate duplicate purchases after the old three-second timeout");
            scenario.Army.Resources = 0; scenario.PumpHost(); executor.Update(Tick());
            Check(executor.State == AIPlanExecutionState.Failed && executor.LastDecision.Contains("Host rejected"),
                "Executor immediately releases a request rejected after resources changed");
            executor.Reset();
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            for (int step = 0; step < 62; step++) executor.Update(Tick());
            Check(executor.State == AIPlanExecutionState.Failed && !executor.IsBusy,
                "An unaffordable plan has a bounded no-progress path rather than locking its controller");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Barracks.ProductionQueue.Enqueue(scenario.NextId(), "gunner", scenario.AI.Id, 300);
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            for (int step = 0; step < 150; step++)
            {
                scenario.Barracks.ProductionQueue.Update(0.5f, out _);
                executor.Update(Tick());
            }
            Check(executor.IsBusy && executor.State == AIPlanExecutionState.InProgress,
                "Long production with continuous progress never trips a fixed wall-clock timeout");
            for (int step = 0; step < 62; step++) executor.Update(Tick());
            Check(executor.State == AIPlanExecutionState.Failed && scenario.Barracks.ProductionQueue.Orders.Count == 1,
                "Frozen production releases its plan but preserves the paid queue");
            scenario.Barracks.ProductionQueue.ApplyState(new([]));
            executor.Reset();
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            scenario.Army.Resources = 10000;
            executor.Update(Tick()); scenario.Network.Update(); scenario.PumpHost(); executor.Update(Tick());
            scenario.Remove(scenario.Barracks); executor.Update(Tick());
            Check(executor.State == AIPlanExecutionState.Failed,
                "Destroyed confirmed producer fails immediately without buying another duplicate");
        }
        using (var scenario = new Scenario(false))
        using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
        {
            var site = scenario.AddBuilding("reaktor", new(35.5f, 0, 35.5f), completed: false);
            for (int step = 0; step < 65; step++) { monitor.Update(Tick()); scenario.Network.Update(); }
            Check(scenario.Messages.Count(m => m.Type == NetworkMessageType.BuildConstructionRequest &&
                m.ConstructionSiteId == site.UnitId) == 2,
                "Stalled legacy construction retries a bounded number of ordinary construction requests");
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.CancelConstructionRequest && m.UnitId == site.UnitId),
                "Repeatedly unreachable construction uses the normal host cancellation request");
            Check(monitor.AvoidSite(new(35, 35)) && monitor.AvoidSite(new(36, 35)) && !monitor.AvoidSite(new(50, 50)),
                "Failed construction temporarily excludes the site and nearby replacement sites");
            scenario.PumpHost();
            Check(scenario.World.Units.FindById(site.UnitId) is Building { IsDying: true },
                "The real host applies cancellation to the stalled construction");
            for (int step = 0; step < 61; step++) monitor.Update(Tick());
            Check(!monitor.AvoidSite(new(35, 35)), "Construction site cooldown expires");
        }
        using (var scenario = new Scenario(false))
        using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
        {
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            var unit = scenario.Soldiers[0];
            var target = new Vector3(40.5f, 0, 40.5f);
            commands.GotoAsync([unit.UnitId], target).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost(); scenario.Messages.Clear();
            for (int step = 0; step < 33; step++)
            {
                monitor.Update(Tick()); scenario.Network.Update(); scenario.PumpHost();
                // Intentional immobility: no movement simulation is advanced.
            }
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest) &&
                scenario.Messages.Any(m => m.Type == NetworkMessageType.StopRequest),
                "Motionless unit is replanned once and then stopped rather than retrying forever");
            Check(unit.CurrentCommand is null, "Host applied watchdog Stop releases the actual movement command");
            commands.GotoAsync([unit.UnitId], target).GetAwaiter().GetResult();
            Check(commands.LastRequest!.State == LocalRequestState.Rejected,
                "Refreshing the same unreachable objective during cooldown is blocked locally");
            commands.GotoAsync([unit.UnitId], new(30.5f, 0, 15.5f)).GetAwaiter().GetResult();
            Check(commands.LastRequest!.State == LocalRequestState.Pending,
                "A genuinely different movement objective is allowed during recovery");
            monitor.Reset();
            commands.GotoAsync([unit.UnitId], target).GetAwaiter().GetResult();
            Check(commands.LastRequest!.State == LocalRequestState.Pending, "Match reset removes failed objective cooldowns");
        }
        using (var scenario = new Scenario(false))
        using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
        {
            scenario.Barracks.ProductionQueue.Enqueue(scenario.NextId(), "gunner", scenario.AI.Id, 300);
            for (int step = 0; step < 62; step++) monitor.Update(Tick());
            Check(monitor.AvoidProducer(scenario.Barracks.UnitId) &&
                scenario.Barracks.ProductionQueue.Orders.Count == 1,
                "Stalled producer is temporarily avoided without deleting purchased orders");
            scenario.Barracks.ProductionQueue.Update(1, out _); monitor.Update(Tick());
            Check(!monitor.AvoidProducer(scenario.Barracks.UnitId),
                "A recovering producer becomes available as soon as actual progress resumes");
        }
        using (var scenario = new Scenario(false))
        using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
        {
            scenario.Army.Resources = 10000;
            var first = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            var second = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            first.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            second.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            scenario.Network.Update();
            Check(ReferenceEquals(first.LastRequest, second.LastRequest) &&
                scenario.Messages.Count(m => m.Type == NetworkMessageType.TrainUnitRequest) == 1,
                "Independent AI controllers share an outstanding identical purchase instead of paying twice");
            scenario.PumpHost();
            Check(scenario.Barracks.ProductionQueue.Orders.Count == 1 && scenario.Army.Resources == 9900,
                "Deduplicated purchase creates one actual host order and one resource charge");
            scenario.Home.ProductionQueue.Enqueue(scenario.NextId(), ResearchProjects.AirTechnologyId, scenario.AI.Id, 300);
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            executor.Start(new([new(AIProductionPlanStepKind.Research, ResearchProjects.AirTechnologyId, "gdi-base")]));
            executor.Update(Tick()); scenario.Network.Update();
            Check(executor.State == AIPlanExecutionState.InProgress &&
                !scenario.Messages.Any(m => m.Type == NetworkMessageType.ResearchRequest),
                "Replanning adopts already paid research without requesting it a second time");
            var site = scenario.AddBuilding("reaktor", new(35.5f, 0, 35.5f), completed: false);
            executor.Reset(); executor.Start(new([new(AIProductionPlanStepKind.BuildBuilding, "reaktor", "gdi-bulldozer")]));
            executor.Update(Tick()); scenario.Network.Update();
            Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.BuildRequest) &&
                executor.LastDecision.Contains("Adopted"),
                "Existing unfinished construction is adopted instead of purchased again");
            for (int step = 0; step < 85; step++)
            {
                site.AdvanceConstruction(0.25f);
                monitor.Update(Tick()); scenario.Network.Update();
            }
            Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.CancelConstructionRequest && m.UnitId == site.UnitId),
                "Slow but genuine building progress is never cancelled by the safety net");
        }
        foreach (bool staging in new[] { false, true })
        using (var scenario = new Scenario(false))
        {
            Vector3 position = staging ? new(40.5f, 0, 40.5f) : new(8.5f, 0, 14.5f);
            var leader = scenario.Add(new SquadLeader(position, scenario.NextId(), loadModel: false));
            var medic = scenario.Add(new Medic(position + Vector3.UnitX, scenario.NextId(), loadModel: false));
            var members = scenario.Soldiers.Skip(1).Append<Soldier>(medic).ToArray();
            scenario.ConfirmSquad(leader, members, UnitActionType.AssembleSquad);
            Guid enemyArmy = Guid.NewGuid();
            scenario.World.SimulationArmies.EnsureArmy(enemyArmy, Guid.NewGuid(), Guid.NewGuid());
            var target = scenario.Add(new CatalogBuilding("vehicle-factory", new(58.5f, 0, 58.5f), scenario.NextId()), enemyArmy);
            scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(58, 58), 0);
            var profile = AIStrategyProfile.Create(1234, scenario.Army.Id) with
            { RequiredGunners = 3, RequiredRakZero = 0, AttackReadinessSeconds = 0, AssaultStallTimeoutSeconds = 3 };
            var assault = new AISquadAssaultController(scenario.World, scenario.AI.Id, scenario.Army.Id, scenario.Network, profile);
            assault.Update(Tick()); scenario.Network.Update();
            Check(assault.State == (staging ? AISquadAssaultState.Staging : AISquadAssaultState.Advancing),
                "Fixture begins the intended squad movement phase");
            for (int step = 0; step < 5; step++) { assault.Update(Tick()); scenario.Network.Update(); }
            Check(staging ? assault.State == AISquadAssaultState.Retreating :
                    assault.State is AISquadAssaultState.Retreating or AISquadAssaultState.MissionComplete,
                staging ? "Repeated staging requests cannot reset squad progress deadline" :
                    "Repeated attacks on an unreachable enemy trigger retreat");
            for (int step = 0; step < 6; step++) { assault.Update(Tick()); scenario.Network.Update(); }
            Check(assault.State == AISquadAssaultState.MissionComplete,
                "Blocked retreat releases the mission instead of indefinitely holding the squad cycle");
            assault.BeginNextMission(); assault.Update(Tick());
            Check(assault.State == AISquadAssaultState.WaitingForKnownTarget,
                "The next mission temporarily avoids the same failed attack objective");
        }
        using (var scenario = new Scenario(false))
        {
            var leader = scenario.Add(new SquadLeader(new(8.5f, 0, 14.5f), scenario.NextId(), loadModel: false));
            var medic = scenario.Add(new Medic(new(9.5f, 0, 14.5f), scenario.NextId(), loadModel: false));
            scenario.ConfirmSquad(leader, scenario.Soldiers.Skip(1).Append<Soldier>(medic), UnitActionType.AssembleSquad);
            var recovery = new AISquadRecoveryController(scenario.World, scenario.AI.Id, scenario.Army.Id, scenario.Network);
            Check(recovery.Begin(), "Recovery progress fixture starts with a real squad");
            scenario.Remove(medic); recovery.Update(Tick());
            Check(recovery.HasFailed && recovery.IsFinished && !recovery.IsRecovered && recovery.LastDecision.Contains("cannot progress"),
                "Losing the medic releases recovery for reinforcement instead of locking the squad cycle");
        }
        using (var scenario = new Scenario(false))
        using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
        {
            var harvester = scenario.World.Units.GetArmyUnits(scenario.Army.Id).OfType<Harvester>().Single();
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.GotoAsync([harvester.UnitId], new(45.5f, 0, 45.5f)).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost();
            harvester.ApplyHarvestState(HarvestPhase.DrivingToField, 10);
            scenario.Messages.Clear();
            for (int step = 0; step < 17; step++) { monitor.Update(Tick()); scenario.Network.Update(); }
            Check(scenario.Messages.Any(m => m.Type == NetworkMessageType.HarvestRequest && m.UnitId == harvester.UnitId) &&
                !scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoRequest),
                "Harvest movement recovery preserves the autonomous harvest job through HarvestRequest");
        }
        using (var scenario = new Scenario(false))
        using (var monitor = new AIOrderProgressMonitor(scenario.World, scenario.AI.Player, scenario.Network))
        {
            var preview = new CatalogBuilding("reaktor", Vector3.Zero, scenario.NextId(), completed: false);
            preview.SetArmy(scenario.Army.Id);
            var search = typeof(ArmyGoalController).GetMethod("TryFindBuildingSite",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            bool FindSite() => (bool)search.Invoke(null,
                new object[] { scenario.World, preview, new Vector3(35.5f, 0, 35.5f), 0, 1, Vector3.Zero })!;
            Check(FindSite(), "Rejected-build test starts with a locally valid construction area");
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.BuildAndConstructAsync("reaktor", new(35.5f, 0, 35.5f), 0,
                [scenario.World.Units.GetArmyUnits(scenario.Army.Id).OfType<Builder>().Single().UnitId]).GetAwaiter().GetResult();
            scenario.Network.Update(); scenario.PumpHost(); monitor.Update(Tick());
            Check(commands.LastRequest!.State == LocalRequestState.Rejected &&
                monitor.LastDecision?.StartsWith("Host rejected") == true,
                "Rejected initial construction is diagnosed even without an existing world job");
            Check(FindSite() && commands.LastRequest!.Result.Failure == AIOrderFailure.Resources,
                "Resource rejection preserves a valid area instead of incorrectly blacklisting its terrain");
            Guid firstBuildingId = commands.BuildAndConstructAsync("reaktor", new(45.5f, 0, 45.5f), 0, []).GetAwaiter().GetResult();
            Guid duplicateBuildingId = commands.BuildAndConstructAsync("reaktor", new(45.5f, 0, 45.5f), 0, []).GetAwaiter().GetResult();
            scenario.Network.Update();
            Check(firstBuildingId == duplicateBuildingId && scenario.Messages.Count(m =>
                m.Type == NetworkMessageType.BuildRequest && m.X == 45.5f) == 1,
                "Unconfirmed identical building requests share their original building identity and purchase");
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            scenario.Army.Resources = 10000;
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            executor.Update(Tick()); scenario.Network.Update();
            // The request has been admitted, but not processed by the host yet.
            scenario.Network.Disconnect(); scenario.Network.CreateSessionAsync("Restarted checks").GetAwaiter().GetResult();
            scenario.Host.Update(new GameTime()); executor.Update(Tick());
            Check(executor.State == AIPlanExecutionState.Failed && executor.LastDecision.Contains("session changed"),
                "Session changes release a pending plan instead of waiting for an old host forever");
        }
        return checks;
    }
}
