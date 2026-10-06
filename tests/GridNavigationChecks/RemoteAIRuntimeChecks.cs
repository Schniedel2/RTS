using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static partial class AIReconstructionChecks
{
    public static int RunRemoteRuntimeChecks()
    {
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new Exception("Remote AI: " + reason); checks++; }
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        void Apply(NetworkHandler target, AIControllerAssignment assignment) => typeof(AIControllerAssignments).GetMethod("Apply", hidden)!.Invoke(target.AIControllers, [assignment]);
        using var scenario = new Scenario(false);
        using var client = new NetworkHandler("remote runtime fixture");
        using var runtime = new RemoteAIRuntime();
        var profile = AIStrategyProfile.Create(731, scenario.Army.Id) with { ResourceReserve = 137, DecisionIntervalSeconds = 10 };
        var assignment = new AIControllerAssignment(scenario.Army.Id, scenario.AI.Id, client.LocalPeerId, 7, profile);
        Check(!client.CanRunAI(scenario.Army.Id), "unassigned client cannot run decisions");
        Apply(client, assignment);
        runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.Count == 0 && !client.CanRunAI(scenario.Army.Id), "assignment alone cannot start before synchronization");
        typeof(NetworkHandler).GetProperty("Status")!.SetValue(client, NetworkConnectionStatus.Synchronizing);
        runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.Count == 0, "synchronizing client stays idle");
        typeof(NetworkHandler).GetProperty("Status")!.SetValue(client, NetworkConnectionStatus.Connected);
        Check(client.CanRunAI(scenario.Army.Id), "ready assigned client can run decisions");
        runtime.Update(new GameTime(), scenario.World, client);
        var running = runtime.Players.Single();
        Check(running.Controller.StrategyProfile == profile, "resolved host profile and seed are used verbatim");
        Check(running.Player.ArmyId == scenario.Army.Id && running.Id == assignment.ActorId, "virtual actor stays separate from controller peer");
        var missing = assignment with { ArmyId = Guid.NewGuid(), ActorId = Guid.NewGuid() };
        Apply(client, missing);
        runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.Count == 1, "no decisions without replicated army context");
        var otherArmy = scenario.World.SimulationArmies.EnsureArmy(missing.ArmyId, missing.ActorId);
        runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.Count == 2, "one peer runs two independent army contexts");
        Apply(client, assignment with { Generation = 8 });
        runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.Single(p => p.Id == running.Id) != running && running.Status == AIPlayerStatus.Idle,
            "generation change releases old local planning before replacement");
        Apply(client, assignment with { Generation = 9, ControllerPeerId = scenario.Network.LocalPeerId });
        runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.All(p => p.Id != running.Id), "assignment elsewhere stops local decisions");
        Apply(scenario.Network, assignment);
        scenario.Messages.Clear();
        scenario.AI.Controller.Update(new GameTime(), scenario.AI, scenario.World, scenario.Network);
        Check(scenario.Network.PendingMessages == 0 && !scenario.Network.CanRunAI(scenario.Army.Id), "host never duplicates remote decisions");
        client.Disconnect(); runtime.Update(new GameTime(), scenario.World, client);
        Check(runtime.Players.Count == 0, "session disconnect releases all local controllers");

        // Queue execution comes from feedback, not an absent host-local production object.
        using var replica = new NetworkHandler("queue replica");
        Apply(replica, assignment with { ControllerPeerId = replica.LocalPeerId });
        typeof(NetworkHandler).GetProperty("Status")!.SetValue(replica, NetworkConnectionStatus.Connected);
        using var queue = new AIOrderQueue(scenario.World, scenario.AI.Player, replica);
        NetworkMessage request = NetworkCommands.CreateTrainUnitRequest(scenario.AI.Id, scenario.Barracks.UnitId, "gunner") with { RequestId = Guid.NewGuid() };
        var receipt = new RequestReceipt(request, replica.SessionGeneration);
        var order = new AIOrderQueue.Order(request, receipt, AIOrderPriority.Production, false);
        typeof(AIOrderQueue.Order).GetField("Sent", hidden)!.SetValue(order, true);
        typeof(AIOrderQueue.Order).GetProperty("ReservedResources")!.SetValue(order, 100);
        ((System.Collections.Generic.List<AIOrderQueue.Order>)typeof(AIOrderQueue).GetField("_orders", hidden)!.GetValue(queue)!).Add(order);
        queue.Dispatch(false);
        Check(order.ReservedResources == 100, "pending wire request holds budget reservation");
        typeof(RequestReceipt).GetProperty("State")!.SetValue(receipt, LocalRequestState.Accepted);
        queue.Dispatch(false);
        Check(order.State == AIQueuedOrderState.InProgress && order.ReservedResources == 0,
            "host acknowledgment releases reservation without inventing completion from missing replica queue");
        typeof(AIOrderResult).GetMethod("Set", hidden)!.Invoke(receipt.Result, [AIOrderStatus.Completed, AIOrderFailure.None, null]);
        queue.Dispatch(false);
        Check(order.State == AIQueuedOrderState.Completed, "host completion finishes remote queue order");
        var rejectedReceipt = new RequestReceipt(request with { RequestId = Guid.NewGuid() }, replica.SessionGeneration);
        var rejected = new AIOrderQueue.Order(rejectedReceipt.Request, rejectedReceipt, AIOrderPriority.Production, false);
        typeof(AIOrderQueue.Order).GetProperty("ReservedResources")!.SetValue(rejected, 100);
        ((System.Collections.Generic.List<AIOrderQueue.Order>)typeof(AIOrderQueue).GetField("_orders", hidden)!.GetValue(queue)!).Add(rejected);
        typeof(RequestReceipt).GetProperty("State")!.SetValue(rejectedReceipt, LocalRequestState.Rejected);
        queue.Dispatch(false);
        Check(rejected.State == AIQueuedOrderState.Failed && rejected.ReservedResources == 0, "rejection frees remote budget");
        var continuation = NetworkCommands.CreateBuildConstructionRequest(scenario.AI.Id, [], scenario.Home.UnitId) with
        {
            RequestId = Guid.NewGuid(), AIControllerArmyId = assignment.ArmyId,
            AIControllerActorId = assignment.ActorId, AIControllerGeneration = assignment.Generation,
            ControllerPeerId = replica.LocalPeerId
        };
        var continuationReceipt = new RequestReceipt(continuation, replica.SessionGeneration);
        typeof(RequestReceipt).GetProperty("State")!.SetValue(continuationReceipt, LocalRequestState.Accepted);
        var paused = new AIOrderQueue.Order(continuation, continuationReceipt, AIOrderPriority.Production, false);
        typeof(AIOrderQueue.Order).GetField("Sent", hidden)!.SetValue(paused, true);
        typeof(AIOrderQueue.Order).GetProperty("State")!.SetValue(paused, AIQueuedOrderState.Paused);
        ((System.Collections.Generic.List<AIOrderQueue.Order>)typeof(AIOrderQueue).GetField("_orders", hidden)!.GetValue(queue)!).Add(paused);
        queue.Dispatch();
        var resumed = (RequestReceipt?)typeof(AIOrderQueue.Order).GetField("ExecutionReceipt", hidden)!.GetValue(paused);
        Check(resumed?.Request.AIControllerArmyId == assignment.ArmyId && resumed.Request.AIControllerActorId == assignment.ActorId &&
            resumed.Request.AIControllerGeneration == assignment.Generation && resumed.Request.ControllerPeerId == replica.LocalPeerId,
            "resumed construction retains original controller token");
        var built = scenario.World.Units.SpawnBuilding("reaktor", new(45.5f, 0, 45.5f), 0, Guid.NewGuid(), Guid.NewGuid(), 500, scenario.Army.Id);
        Check(built?.ArmyId == scenario.Army.Id, "confirmed building army does not depend on a local player record");
        typeof(GameWorld).GetMethod("ConfigureSimulation", hidden)!.Invoke(scenario.World, [scenario.World.SimulationArmies, replica, null]);
        var sites = typeof(GameWorld).GetProperty("RemoteBuildSites", hidden)!.GetValue(scenario.World)!;
        var find = sites.GetType().GetMethod("TryFind")!;
        var preview = BuildingFactory.SpawnBuilding("reaktor", Vector3.Zero, 0, Guid.NewGuid(), scenario.AI.Id)!;
        preview.SetArmy(scenario.Army.Id);
        object[] query = [preview, new Vector3(30.5f, 0, 30.5f), 5, 15, Vector3.Zero];
        Check(!(bool)find.Invoke(sites, query)!, "first client build-site query only schedules work");
        var targets = scenario.World.ScoutingTargets;
        var planning = (PlanningScheduler)typeof(ScoutingTargets).GetProperty("ClientPlanning", hidden)!.GetValue(targets)!;
        planning.Update(1, double.PositiveInfinity);
        Check(planning.LastSteps == 1 && !(bool)find.Invoke(sites, query)!, "site precheck honors shared step budget");
        planning.Update(2048, double.PositiveInfinity);
        Check((bool)find.Invoke(sites, query)!, "budgeted client precheck eventually returns valid site");
        find.Invoke(sites, query);
        sites.GetType().GetMethod("Clear")!.Invoke(sites, [scenario.Army.Id]);
        planning.Update(2048, double.PositiveInfinity);
        Check(planning.PendingJobs == 0, "clearing army invalidates queued site work");
        return checks;
    }
}
