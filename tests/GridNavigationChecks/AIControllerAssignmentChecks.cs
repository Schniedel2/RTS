using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;
internal static partial class AIReconstructionChecks
{
    public static int RunControllerAssignmentChecks()
    {
        int checks = 0;
        void Check(bool condition, string text) { if (!condition) throw new Exception("AI assignment: " + text); checks++; }
        using var scenario = new Scenario(false);
        NetworkHandler network = scenario.Network;
        Guid actor = scenario.AI.Id, army = scenario.Army.Id, peer = network.LocalPeerId;
        var profile = AIStrategyProfile.Create(42, army);
        var first = network.AssignAIController(army, actor, peer, profile);
        bool Authorized(NetworkMessage message) => (bool)typeof(NetworkHost).GetMethod("AuthorizeController", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scenario.Host, [message])!;
        var move = NetworkCommands.CreateGotoRequest(actor, [scenario.Soldiers[0].UnitId], 20, 0, 20) with {
            AIControllerArmyId = army, AIControllerActorId = actor, ControllerPeerId = peer, AIControllerGeneration = first.Generation };
        Check(Authorized(move), "valid host controller can command own soldier");
        Check(!Authorized(move with { AIControllerGeneration = first.Generation + 1 }), "future generation denied");
        Check(!Authorized(move with { ControllerPeerId = Guid.NewGuid() }), "wrong peer denied");
        Check(!Authorized(move with { AIControllerActorId = Guid.NewGuid() }), "wrong virtual actor denied");
        Check(!Authorized(NetworkCommands.CreateGotoRequest(actor, [scenario.Soldiers[0].UnitId], 20, 0, 20)), "ordinary actor request cannot bypass assignment");
        var otherArmy = scenario.World.SimulationArmies.EnsureArmy(Guid.NewGuid(), Guid.NewGuid());
        var foreign = scenario.Add(new Gunner(new Vector3(40.5f, 0, 40.5f), scenario.NextId()), otherArmy.Id);
        Check(!Authorized(move with { UnitIds = [scenario.Soldiers[0].UnitId, foreign.UnitId] }), "all group recipients constrained to assigned army");
        Check(!Authorized(move with { ArmyId = otherArmy.Id }), "explicit other army denied");
        Check(!Authorized(move with { UnitId = foreign.UnitId }), "secondary recipient field cannot bypass army check");
        Check(!Authorized(move with { Type = NetworkMessageType.GrantArmyControlRequest }), "AI cannot grant human permission");
        var gateway = new PlayerCommandService(network, actor);
        gateway.StopAsync([scenario.Soldiers[0].UnitId]).GetAwaiter().GetResult();
        var captured = gateway.LastRequest!.Request;
        Check(captured.AIControllerGeneration == first.Generation && captured.AIControllerArmyId == army, "gateway captures token");
        var revoked = network.AssignAIController(army, actor, null, profile);
        Check(revoked.Generation > first.Generation && !Authorized(captured), "revocation invalidates already created command");
        var renewed = network.AssignAIController(army, actor, peer, profile);
        gateway.StopAsync([scenario.Soldiers[0].UnitId]).GetAwaiter().GetResult();
        Check(gateway.LastRequest!.Request.AIControllerGeneration == first.Generation && !Authorized(gateway.LastRequest.Request), "old gateway cannot adopt new generation");
        Check(Authorized(move with { AIControllerGeneration = renewed.Generation }), "fresh generation accepted");
        network.AssignAIController(otherArmy.Id, otherArmy.OwnerPlayerIds.First(), peer, AIStrategyProfile.Create(42, otherArmy.Id));
        Check(network.AIControllers.Snapshot().Count(a => a.ControllerPeerId == peer) == 2, "one peer may control multiple distinct armies");
        var json = JsonSerializer.Serialize(network.AIControllers.Snapshot());
        var restore = typeof(AIControllerAssignments).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var replica = new AIControllerAssignments();
        restore.Invoke(replica, [JsonSerializer.Deserialize<AIControllerAssignment[]>(json)!]);
        Check(replica.Snapshot().SequenceEqual(network.AIControllers.Snapshot()), "resolved profiles and generations survive snapshot serialization");
        try { restore.Invoke(replica, [new[] { renewed, renewed }]); throw new Exception("Duplicate snapshot accepted"); }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { checks++; }
        Check(replica.Find(army) == renewed, "invalid snapshot cannot partially replace registry");
        using var client = new NetworkHandler("unauthorized controller");
        try { client.AssignAIController(army, actor, client.LocalPeerId, profile); throw new Exception("Client self-assignment accepted"); }
        catch (InvalidOperationException) { checks++; }
        try { network.AssignAIController(army, actor, Guid.NewGuid(), profile); throw new Exception("Unknown peer accepted"); }
        catch (ArgumentException) { checks++; }
        network.Disconnect();
        Check(network.AIControllers.Snapshot().Length == 0, "session disconnect clears controller grants");
        return checks;
    }
}
