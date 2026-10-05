using System;
using System.Linq;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    private static int RunOrderResultChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 10000;
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            AIOrderResult result = commands.LastRequest!.Result;
            Check(ReferenceEquals(result, queue.Orders[0].Result) && result.Status == AIOrderStatus.Requested,
                "Gateway receipt and army order share one requested outcome");
            scenario.Network.Update(); scenario.PumpHost();
            Check(result.Status == AIOrderStatus.Accepted && result.WasAccepted, "Real host acknowledgment explicitly records Accepted");
            queue.Dispatch();
            Check(result.Status == AIOrderStatus.InProgress && !result.IsTerminal, "Accepted FIFO is a running job");
            scenario.Barracks.ProductionQueue.Update(100, out _); queue.Dispatch();
            Check(result.Status == AIOrderStatus.Completed && result.Failure == AIOrderFailure.None,
                "Exact production completion produces Completed");
            Check(result.Transitions.SequenceEqual(new[] { AIOrderStatus.Requested, AIOrderStatus.Accepted,
                AIOrderStatus.InProgress, AIOrderStatus.Completed }), "Lifecycle retains acceptance separately from later progress");
            result.Set(AIOrderStatus.Failed, AIOrderFailure.Timeout, "late feedback");
            Check(result.Status == AIOrderStatus.Completed, "Late feedback cannot overwrite terminal success");
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "engineer").GetAwaiter().GetResult();
            result = commands.LastRequest!.Result;
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            scenario.Barracks.ProductionQueue.ApplyState(new([])); queue.Dispatch();
            Check(result.Status == AIOrderStatus.Failed && result.Failure == AIOrderFailure.Cancelled,
                "Disappearing FIFO is cancellation rather than invented completion");
        }
        foreach (AIOrderFailure expected in new[] { AIOrderFailure.Resources, AIOrderFailure.Producer,
            AIOrderFailure.Perk, AIOrderFailure.BuildSite, AIOrderFailure.InvalidTarget })
        {
            using var scenario = new Scenario(false);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            scenario.Army.Resources = 10000;
            switch (expected)
            {
                case AIOrderFailure.Resources:
                    scenario.Army.Resources = 0;
                    commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult(); break;
                case AIOrderFailure.Producer:
                    commands.TrainUnitAsync(Guid.NewGuid(), "gunner").GetAwaiter().GetResult(); break;
                case AIOrderFailure.Perk:
                    commands.BuildAsync("helipad", new(40.5f, 0, 40.5f), 0).GetAwaiter().GetResult(); break;
                case AIOrderFailure.BuildSite:
                    commands.BuildAsync("reaktor", new(100.5f, 0, 100.5f), 0).GetAwaiter().GetResult(); break;
                default:
                    commands.ConstructAsync([scenario.Soldiers[0].UnitId], Guid.NewGuid()).GetAwaiter().GetResult(); break;
            }
            AIOrderResult result = commands.LastRequest!.Result;
            scenario.Network.Update(); scenario.PumpHost();
            Check(result.Status == AIOrderStatus.Rejected && result.Failure == expected && result.Reason is not null,
                $"Real host rejection stores {expected} distinctly: {result.Status}/{result.Failure}/{result.Reason}");
            Check(!result.WasAccepted && result.IsTerminal, $"Rejected {expected} was never accepted or in progress");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            AIOrderResult result = commands.LastRequest!.Result;
            queue.Update(61);
            Check(result.Status == AIOrderStatus.Failed && result.Failure == AIOrderFailure.Timeout,
                "Scheduler timeout is Failed/Timeout rather than fabricated host rejection");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 10000;
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            executor.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            scenario.Add(new Gunner(new(35.5f, 0, 35.5f), scenario.NextId()));
            executor.Update(new GameTime());
            Check(executor.State == AIPlanExecutionState.InProgress,
                "An unrelated gunner cannot falsely complete this training plan");
            scenario.Barracks.ProductionQueue.Update(100, out _); queue.Dispatch(); executor.Update(new GameTime());
            Check(executor.State == AIPlanExecutionState.Completed,
                "Planner advances on its shared exact order result without counting unrelated units");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            MobileUnit worker = scenario.World.Units.GetArmyUnits(scenario.Army.Id).OfType<MobileUnit>()
                .Single(unit => unit.BuildRate > 0);
            Building site = scenario.AddBuilding("reaktor", new(40.5f, 0, 40.5f), false);
            commands.ConstructAsync([worker.UnitId], site.UnitId).GetAwaiter().GetResult();
            AIOrderResult result = commands.LastRequest!.Result;
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(result.Status == AIOrderStatus.InProgress && result.WasAccepted,
                "Valid host construction has a shared running result");
            commands.ConstructAsync([worker.UnitId], site.UnitId).GetAwaiter().GetResult();
            scenario.Remove(site);
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            Check(result.Status == AIOrderStatus.Failed && result.Failure == AIOrderFailure.InvalidTarget && result.WasAccepted,
                "Rejected execution retry fails the accepted owner job and retains its acceptance history");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Army.Resources = 10000;
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            AIOrderResult result = commands.LastRequest!.Result;
            scenario.Network.Update(); scenario.PumpHost(); queue.Dispatch();
            scenario.Remove(scenario.Barracks); queue.Dispatch();
            Check(result.Status == AIOrderStatus.Failed && result.Failure == AIOrderFailure.Producer,
                "Loss of an accepted producer stores a producer failure rather than host rejection");
        }
        using (var scenario = new Scenario(false))
        {
            using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, scenario.Network);
            var commands = new PlayerCommandService(scenario.Network, scenario.AI.Id);
            commands.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
            AIOrderResult result = commands.LastRequest!.Result;
            scenario.Network.Disconnect(); queue.Dispatch();
            Check(result.Status == AIOrderStatus.Failed && result.Failure == AIOrderFailure.SessionChanged,
                "Session generation changes retain a specific common failure reason");
        }
        using (var scenario = new Scenario(false))
        {
            scenario.Barracks.ProductionQueue.Enqueue(scenario.NextId(), "gunner", scenario.AI.Id, 5);
            var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, scenario.Network);
            executor.Start(new([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
            executor.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            scenario.Add(new Gunner(new(35.5f, 0, 35.5f), scenario.NextId())); executor.Update(new GameTime());
            Check(executor.State == AIPlanExecutionState.InProgress,
                "Adopted FIFO also ignores unrelated units without an original local request");
            scenario.Barracks.ProductionQueue.Update(5, out _); executor.Update(new GameTime());
            Check(executor.State == AIPlanExecutionState.Completed, "Adopted exact order completes its plan");
        }
        return checks;
    }
}

