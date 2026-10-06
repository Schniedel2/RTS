using RTS;
using RTS.Network;
using System.Reflection;
using System.Text.Json;
internal static class RequestFeedbackChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("Request feedback: " + reason); checks++; }
        object? Call(NetworkHandler network, string name, params object[] args) => typeof(NetworkHandler).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length && (name != "ReportRequestExecution" || m.GetParameters()[0].ParameterType == typeof(NetworkMessage))).Invoke(network, args);
        using var host = new NetworkHandler("feedback host");
        int port = host.CreateSessionAsync("feedback checks").GetAwaiter().GetResult();
        using var client = new NetworkHandler("feedback client");
        Guid actor = client.LocalPeerId, order = Guid.NewGuid();
        var request = NetworkCommands.CreateTrainUnitRequest(actor, Guid.NewGuid(), "gunner") with { ProductionOrderId = order };
        var receipt = (RequestReceipt)Call(client, "TrackLocalRequest", request)!;
        Check(receipt.State == LocalRequestState.Pending && receipt.RequestId != Guid.Empty, "remote request tracked before transport");
        var jsonOptions = (JsonSerializerOptions)typeof(NetworkHandler).GetField("_jsonOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        NetworkMessage Wire(NetworkMessage message) => JsonSerializer.Deserialize<NetworkMessage>(JsonSerializer.Serialize(message, jsonOptions), jsonOptions)!;
        NetworkMessage original = Wire(request);
        Check(original.RequestId == receipt.RequestId && original.ProductionOrderId == order, "wire preserves correlation");
        Check((bool)Call(host, "AdmitRequestId", original)!, "first arrival admitted");
        Check(!(bool)Call(host, "AdmitRequestId", Wire(request))!, "duplicate while pending cannot execute");
        Call(host, "ResolveLocalRequest", original, true, null!, AIOrderFailure.None);
        var cache = (System.Collections.IDictionary)typeof(NetworkHandler).GetField("_hostRequestFeedback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        NetworkMessage Feedback() => Wire((NetworkMessage)cache[(actor, receipt.RequestId)]!);
        Call(client, "ApplyRequestFeedback", Feedback());
        Check(receipt.State == LocalRequestState.Accepted && receipt.Result.Status == AIOrderStatus.Accepted, "accepted does not mean completed");
        Check(Feedback().RequestFeedback!.ProductionOrderId == order, "feedback production correlation");
        Check(!(bool)Call(host, "AdmitRequestId", Wire(request))!, "lost answer retry cannot repurchase");
        Call(host, "ReportRequestExecution", original, AIOrderStatus.InProgress, AIOrderFailure.None, null!);
        Call(client, "ApplyRequestFeedback", Feedback());
        Check(receipt.Result.Status == AIOrderStatus.InProgress, "remote execution progress");
        var lateAccepted = Feedback() with { RequestFeedback = Feedback().RequestFeedback! with { Status = AIOrderStatus.Accepted } };
        Call(client, "ApplyRequestFeedback", lateAccepted);
        Check(receipt.Result.Status == AIOrderStatus.InProgress, "late acceptance does not regress progress");
        Call(host, "ReportRequestExecution", original, AIOrderStatus.Completed, AIOrderFailure.None, null!);
        Call(client, "ApplyRequestFeedback", Feedback());
        Check(receipt.Result.Status == AIOrderStatus.Completed, "remote completion");
        Call(client, "ApplyRequestFeedback", lateAccepted);
        Check(receipt.Result.Status == AIOrderStatus.Completed, "terminal result stable");
        Check(!(bool)Call(host, "AdmitRequestId", Wire(request))!, "completed request remains deduplicated");
        var invalid = NetworkCommands.CreateTrainUnitRequest(actor, Guid.NewGuid(), "gunner");
        var rejected = (RequestReceipt)Call(client, "TrackLocalRequest", invalid)!;
        Call(host, "AdmitRequestId", invalid);
        Call(host, "ResolveLocalRequest", invalid, false, "Not enough resources", AIOrderFailure.Resources);
        Call(client, "ApplyRequestFeedback", Wire((NetworkMessage)cache[(actor, rejected.RequestId)]!));
        Check(rejected.State == LocalRequestState.Rejected && rejected.Result.Failure == AIOrderFailure.Resources, "remote rejection reason");
        var wrong = Feedback() with { RequestFeedback = Feedback().RequestFeedback! with { ActorId = Guid.NewGuid() } };
        Call(client, "ApplyRequestFeedback", wrong);
        Check(receipt.Result.Status == AIOrderStatus.Completed, "wrong actor ignored");
        var pending = NetworkCommands.CreateStopRequest(actor, [Guid.NewGuid()]);
        var cancelled = (RequestReceipt)Call(client, "TrackLocalRequest", pending)!;
        Call(client, "AbandonLocalRequest", pending, "Cancelled");
        Call(client, "ApplyRequestFeedback", new NetworkMessage(NetworkMessageType.RequestFeedbackCommand, host.LocalPeerId, TargetId: actor)
        { RequestFeedback = new(cancelled.RequestId, actor, cancelled.Generation, LocalRequestState.Accepted, AIOrderStatus.Accepted, AIOrderFailure.None, null, null, null) });
        Check(cancelled.State == LocalRequestState.Abandoned, "late answer cannot revive cancellation");
        var stale = (RequestReceipt)Call(client, "TrackLocalRequest", NetworkCommands.CreateStopRequest(actor, [Guid.NewGuid()]))!;
        client.Disconnect(); client.Update();
        Check(stale.State == LocalRequestState.Abandoned && stale.Result.Failure == AIOrderFailure.SessionChanged, "session change invalidates outstanding work");
        // Complex envelopes preserve ids too, rather than dropping them with typed payload conversion.
        var build = NetworkCommands.CreateBuildRequest(actor, "reaktor", 1, 0, 1, 0, Guid.NewGuid());
        Call(client, "TrackLocalRequest", build);
        Check(Wire(build).RequestId == build.RequestId && Wire(build).RequestGeneration == build.RequestGeneration, "typed envelope correlation round trip");
        host.SetSessionSnapshotProvider(() => new NetworkMessage(NetworkMessageType.SessionSnapshot, host.LocalPeerId,
            SessionSnapshot: new SessionSnapshot(new WorldData(1, 1, [0], [0f]), [], [], [], 0)));
        client.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
        void Pump(Func<bool> done)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!done() && DateTime.UtcNow < deadline) { host.Update(); client.Update(); System.Threading.Thread.Sleep(5); }
            Check(done(), "loopback feedback arrived before deadline");
        }
        Pump(() => client.Status == NetworkConnectionStatus.Connected);
        int executions = 0; NetworkMessage? delivered = null; bool reject = false;
        host.MessageReceived += message =>
        {
            if (message.Type != NetworkMessageType.StopRequest) return;
            if (!(bool)Call(host, "AdmitRequestId", message)!) return;
            executions++; delivered = message;
            Call(host, "ResolveLocalRequest", message, !reject, reject ? "Denied" : null!, AIOrderFailure.Validation);
        };
        var gateway = new PlayerCommandService(client, client.LocalPeerId);
        gateway.StopAsync([Guid.NewGuid()]).GetAwaiter().GetResult();
        RequestReceipt actual = gateway.LastRequest!;
        Pump(() => actual.State == LocalRequestState.Accepted);
        Check(executions == 1 && actual.Result.WasAccepted, "real TCP acknowledgment reaches original controller");
        client.SendToHostAsync(actual.Request).GetAwaiter().GetResult();
        // Force the timeout reconciliation path without a five-second test sleep.
        var polls = (Dictionary<Guid, double>)typeof(NetworkHandler).GetField("_requestPollTimes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        polls[actual.RequestId] = -10;
        for (int i = 0; i < 20; i++) { host.Update(); client.Update(); System.Threading.Thread.Sleep(5); }
        Check(executions == 1, "timeout and duplicate TCP retries do not execute twice");
        Call(host, "ReportRequestExecution", delivered!, AIOrderStatus.Completed, AIOrderFailure.None, null!);
        Pump(() => actual.Result.Status == AIOrderStatus.Completed);
        reject = true;
        gateway.StopAsync([Guid.NewGuid()]).GetAwaiter().GetResult();
        var denied = gateway.LastRequest!;
        Pump(() => denied.State == LocalRequestState.Rejected);
        Check(denied.Reason == "Denied" && executions == 2, "real TCP rejection preserves reason and actor");
        Guid virtualActor = Guid.NewGuid(), controlledArmy = Guid.NewGuid();
        var assignment = host.AssignAIController(controlledArmy, virtualActor, client.LocalPeerId, AIStrategyProfile.Create(42, controlledArmy));
        Pump(() => client.AIControllers.Find(controlledArmy)?.Generation == assignment.Generation);
        Check(client.AIControllers.Find(controlledArmy) == assignment, "live assignment carries effective profile to remote peer");
        var aiGateway = new PlayerCommandService(client, virtualActor);
        NetworkMessage? aiWire = null;
        host.MessageReceived += message => { if (message.AIControllerActorId == virtualActor) aiWire = message; };
        aiGateway.GotoAsync([Guid.NewGuid()], new Microsoft.Xna.Framework.Vector3(1, 0, 1)).GetAwaiter().GetResult();
        Pump(() => aiWire is not null);
        Check(aiWire!.SenderId == client.LocalPeerId && aiWire.ControllerPeerId == client.LocalPeerId &&
            aiWire.AIControllerActorId == virtualActor && host.AIControllers.Authorizes(aiWire), "transport peer and virtual actor remain separate in typed wire envelope");
        host.AssignAIController(controlledArmy, virtualActor, null, assignment.Profile);
        Pump(() => client.AIControllers.Find(controlledArmy)?.ControllerPeerId is null);
        Check(!host.AIControllers.Authorizes(aiWire), "remote captured token revoked");
        return checks;
    }
}
