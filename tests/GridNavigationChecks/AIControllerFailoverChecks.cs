using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    public static int RunControllerFailoverChecks()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("AI failover: " + reason); checks++; }
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        using var scenario = new Scenario(false);
        var host = scenario.Network;
        double wall = 0;
        typeof(NetworkHandler).GetField("AIClockOverride", hidden)!.SetValue(host, (Func<double>)(() => wall));
        var state = new SessionStateService(scenario.World, scenario.World.SimulationArmies);
        host.SetSessionSnapshotProvider(() => new NetworkMessage(NetworkMessageType.SessionSnapshot, host.LocalPeerId,
            SessionSnapshot: state.Capture(0, true) with { AIControllers = host.AIControllers.Snapshot() }));
        int port = ((IPEndPoint)((TcpListener)typeof(NetworkHandler).GetField("_listener", hidden)!.GetValue(host)!).LocalEndpoint).Port;
        using var client = new NetworkHandler("failover client");
        client.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
        void Pump(Func<bool> until)
        {
            var deadline = DateTime.UtcNow.AddSeconds(4);
            do { host.Update(); client.Update(); if (until()) return; Thread.Sleep(2); } while (DateTime.UtcNow < deadline);
            throw new Exception("Failover TCP deadline");
        }
        Pump(() => client.Status == NetworkConnectionStatus.Connected);
        var profile = AIStrategyProfile.Create(919, scenario.Army.Id);
        AIControllerAssignment Assign() { var a = host.AssignAIController(scenario.Army.Id, scenario.AI.Id, client.LocalPeerId, profile); Pump(() => client.AIControllers.Find(a.ArmyId)?.Generation == a.Generation); return a; }
        void Beat(AIControllerAssignment a, long sequence)
        {
            client.SendToHostAsync(new(NetworkMessageType.AIControllerHeartbeat, client.LocalPeerId)
            { AIControllerArmyId = a.ArmyId, AIControllerActorId = a.ActorId, AIControllerGeneration = a.Generation,
                ControllerPeerId = client.LocalPeerId, AIUpdateSequence = sequence }).GetAwaiter().GetResult();
            // Ensure frame arrival before asserting host-clock behavior.
            for (int i = 0; i < 30; i++) { host.Update(); client.Update(); Thread.Sleep(2); }
        }
        var first = Assign(); Beat(first, 1);
        wall = 9; host.Update();
        Check(host.AIControllers.Find(first.ArmyId) == first, "wall-clock lease tolerates temporary pause");
        scenario.AI.Update(new GameTime(TimeSpan.FromDays(1), TimeSpan.FromHours(1)), scenario.World, host);
        Check(host.AIControllers.Find(first.ArmyId) == first, "accelerated simulation cannot expire real-time lease");
        wall = 10.1; host.Update();
        var fallback = host.AIControllers.Find(first.ArmyId)!;
        Check(fallback.ControllerPeerId == host.LocalPeerId && fallback.Generation > first.Generation && fallback.Profile == profile,
            "missing heartbeat returns army to host with preserved profile and seed");
        Beat(first, 100);
        Check(host.AIControllers.Find(first.ArmyId) == fallback, "late old-generation heartbeat cannot reclaim army");
        Check(host.GetAIControllerHealth(first.ArmyId)?.LastFallbackReason?.Contains("heartbeat") == true, "timeout diagnosed separately");
        var stalled = Assign(); Beat(stalled, 1); wall += 9; Beat(stalled, 1);
        Check(host.AIControllers.Find(first.ArmyId) == stalled, "duplicate heartbeat does not immediately expire lease");
        wall += 1.1; Beat(stalled, 1);
        Check(host.GetAIControllerHealth(first.ArmyId)?.LastFallbackReason?.Contains("stalled") == true,
            "live connection with nonadvancing decision sequence falls back");
        var boot = Assign(); wall += 14.9; host.Update();
        Check(host.AIControllers.Find(first.ArmyId) == boot, "startup grace permits synchronization and first update");
        wall += .2; host.Update();
        Check(host.GetAIControllerHealth(first.ArmyId)?.LastFallbackReason?.Contains("start") == true, "never-started controller diagnosed");
        var healthy = Assign(); Beat(healthy, 1); wall += 9; Beat(healthy, 2); wall += 9; host.Update();
        Check(host.AIControllers.Find(first.ArmyId) == healthy, "advancing heartbeats renew lease");
        // Two armies have independent progress and timeout state.
        Guid otherArmy = Guid.NewGuid(), otherActor = Guid.NewGuid();
        var other = host.AssignAIController(otherArmy, otherActor, client.LocalPeerId, profile);
        Pump(() => client.AIControllers.Find(otherArmy)?.Generation == other.Generation);
        Beat(healthy, 3); Beat(other, 1); wall += 2; Beat(healthy, 4); wall += 8.1; host.Update();
        Check(host.AIControllers.Find(otherArmy)?.ControllerPeerId == host.LocalPeerId && host.AIControllers.Find(first.ArmyId) == healthy,
            "one stalled army does not evict healthy army on same peer");
        scenario.Army.Resources = 10000;
        var gateway = new PlayerCommandService(client, scenario.AI.Id);
        gateway.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
        var pending = gateway.LastRequest!;
        Pump(() => scenario.Messages.Any(m => m.RequestId == pending.RequestId));
        host.AssignAIController(first.ArmyId, first.ActorId, host.LocalPeerId, profile);
        scenario.PumpHost(); Pump(() => pending.State != LocalRequestState.Pending);
        Check(pending.State is LocalRequestState.Rejected or LocalRequestState.Abandoned && scenario.Army.Resources == 10000 && scenario.Barracks.ProductionQueue.Orders.Count == 0,
            "handoff rejects unaccepted purchase without charge or production");
        var paidAssignment = Assign();
        gateway = new PlayerCommandService(client, scenario.AI.Id);
        gateway.TrainUnitAsync(scenario.Barracks.UnitId, "gunner").GetAwaiter().GetResult();
        var paid = gateway.LastRequest!;
        Pump(() => scenario.Messages.Any(m => m.RequestId == paid.RequestId)); scenario.PumpHost();
        Pump(() => paid.State == LocalRequestState.Accepted);
        Guid paidId = paid.Request.ProductionOrderId!.Value; float resourcesAfterPurchase = scenario.Army.Resources;
        host.AssignAIController(first.ArmyId, first.ActorId, host.LocalPeerId, profile);
        Check(scenario.Barracks.ProductionQueue.Orders.Single().OrderId == paidId && scenario.Army.Resources == resourcesAfterPurchase,
            "accepted paid production survives handoff");
        client.SendToHostAsync(paid.Request).GetAwaiter().GetResult();
        for (int i = 0; i < 30; i++) { host.Update(); client.Update(); Thread.Sleep(2); }
        scenario.PumpHost();
        Check(scenario.Barracks.ProductionQueue.Orders.Count == 1 && scenario.Army.Resources == resourcesAfterPurchase,
            "late request from previously accepted generation cannot repurchase");
        var executor = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, host);
        executor.Start(new AIProductionPlan([new(AIProductionPlanStepKind.TrainUnit, "gunner", "gdi-barracks")]));
        scenario.Messages.Clear(); executor.Update(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))); host.Update();
        Check(executor.State == AIPlanExecutionState.InProgress && !scenario.Messages.Any(m => m.Type == NetworkMessageType.TrainUnitRequest),
            "new planner adopts paid production without duplicate purchase");
        scenario.Barracks.ProductionQueue.ApplyCompleted(paidId);
        executor.Update(new GameTime(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        Check(executor.State == AIPlanExecutionState.Completed, "adopted production consumes explicit completion");
        var construction = scenario.AddBuilding("reaktor", new(35.5f, 0, 5.5f), completed: false);
        scenario.Remove(scenario.Reactor);
        float buildingProgress = construction.ConstructionProgress;
        scenario.AI.Update(new GameTime(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)), scenario.World, host); host.Update();
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.BuildRequest) && construction.ConstructionProgress == buildingProgress,
            "controller reconstruction adopts existing construction without buying replacement");
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.HarvestRequest), "existing harvest job is not restarted on handoff");
        Check(scenario.AI.Controller.StrategyProfile == profile && scenario.AI.Controller.Context?.Commands is not null,
            "host decision context is rebuilt with resolved assignment profile");
        // Unaccepted path searches are released immediately; accepted paths are untouched.
        var moving = Assign(); Beat(moving, 1);
        var movingGateway = new PlayerCommandService(client, scenario.AI.Id);
        movingGateway.GotoAsync([scenario.Soldiers[0].UnitId], new Vector3(50.5f, 0, 50.5f)).GetAwaiter().GetResult();
        var movementReceipt = movingGateway.LastRequest!;
        Pump(() => scenario.Messages.Any(m => m.RequestId == movementReceipt.RequestId));
        scenario.Host.Update(new GameTime());
        Check(typeof(NetworkHost).GetField("_pendingGoto", hidden)!.GetValue(scenario.Host) is not null,
            "test leaves a live unaccepted path search at handoff");
        host.AssignAIController(first.ArmyId, first.ActorId, host.LocalPeerId, profile);
        Check(typeof(NetworkHost).GetField("_pendingGoto", hidden)!.GetValue(scenario.Host) is null &&
            scenario.Soldiers[0].MovementStatus != MovementStatus.Planning, "handoff clears stale planner and unit planning intent");
        scenario.Messages.Clear(); scenario.PumpHost();
        Check(!scenario.Messages.Any(m => m.Type == NetworkMessageType.GotoCommand && m.RequestId == movementReceipt.RequestId),
            "late scheduler callback cannot publish old route");
        // Queued research is adopted and a host perk confirmation closes its new plan.
        Guid researchId = Guid.NewGuid();
        scenario.Home.ProductionQueue.Enqueue(researchId, ResearchProjects.AirTechnologyId, scenario.AI.Id, 30);
        var research = new AIProductionPlanExecutor(scenario.World, scenario.AI.Player, host);
        research.Start(new AIProductionPlan([new(AIProductionPlanStepKind.Research, ResearchProjects.AirTechnologyId, "gdi-base")]));
        scenario.Messages.Clear(); research.Update(new GameTime(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1))); host.Update();
        Check(research.State == AIPlanExecutionState.InProgress && !scenario.Messages.Any(m => m.Type == NetworkMessageType.ResearchRequest),
            "new controller adopts already paid research");
        scenario.Army.Perks.GrantPermanent(PerkType.AirTechnology, researchId);
        research.Update(new GameTime(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1)));
        Check(research.State == AIPlanExecutionState.Completed, "adopted research finishes from authoritative perk confirmation");
        // Disconnect followed by a restarted peer cannot resurrect an assignment.
        var disconnected = Assign(); Beat(disconnected, 1); client.Disconnect();
        Pump(() => host.AIControllers.Find(first.ArmyId)?.ControllerPeerId == host.LocalPeerId);
        Check(host.GetAIControllerHealth(first.ArmyId)?.LastFallbackReason?.Contains("disconnected") == true, "disconnect falls back without waiting for heartbeat timeout");
        using var restart = new NetworkHandler("restarted bot");
        restart.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
        for (int i = 0; i < 2000 && restart.Status != NetworkConnectionStatus.Connected; i++) { host.Update(); restart.Update(); Thread.Sleep(1); }
        Check(restart.Status == NetworkConnectionStatus.Connected && !restart.CanRunAI(first.ArmyId), "restarted peer synchronizes but cannot auto-reclaim army");
        var explicitAssignment = host.AssignAIController(first.ArmyId, first.ActorId, restart.LocalPeerId, profile);
        for (int i = 0; i < 2000 && restart.AIControllers.Find(first.ArmyId)?.Generation != explicitAssignment.Generation; i++) { host.Update(); restart.Update(); Thread.Sleep(1); }
        Check(restart.CanRunAI(first.ArmyId) && explicitAssignment.Generation > disconnected.Generation && explicitAssignment.Profile == profile,
            "explicit reassignment enables new peer with fresh generation and same seed");
        return checks;
    }
}
