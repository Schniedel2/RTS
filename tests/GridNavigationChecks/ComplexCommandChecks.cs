using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;

internal static class ComplexCommandChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        bool RejectJson(string json)
        { try { JsonSerializer.Deserialize<NetworkMessage>(json, NetworkJson.Options); return false; } catch (JsonException) { return true; } }
        string Wire(NetworkMessage message) => JsonSerializer.Serialize(message, NetworkJson.Options);
        NetworkMessage Replay(NetworkMessage message) => JsonSerializer.Deserialize<NetworkMessage>(Wire(message), NetworkJson.Options)!;
        Guid sender = Guid.NewGuid(), hostId = Guid.NewGuid(), unit = Guid.NewGuid(), army = Guid.NewGuid();
        var target = new CommandPosition(4.5f, 0, 6.5f);
        UnitRoute[] routes = [new(unit, [new(3, 5), new(4, 6)], 4.5f, 6.5f)];
        ComplexCommandPayload[] payloads =
        [
            new GotoRequestPayload([unit], target, true, routes, 135),
            new GotoCommandPayload(sender, [unit], target, routes, true, 135, Guid.NewGuid()),
            new BuildRequestPayload("Reaktor", target, 45, unit, [Guid.NewGuid()]),
            new BuildCommandPayload(sender, army, unit, "Reaktor", target, 45, 725, 9275, [Guid.NewGuid()]),
            new HarvestRequestPayload(unit, target),
            new HarvestCommandPayload(unit, HarvestPhase.Unloading, 284.6f)
        ];
        foreach (ComplexCommandPayload payload in payloads)
        {
            var message = ComplexCommandPayloads.Create(sender, payload, 12.25);
            string json = Wire(message);
            NetworkMessage replay = Replay(message);
            Check(Wire(replay) == json, "Typed " + message.Type + " roundtrips all data and timestamp");
            JsonObject root = JsonNode.Parse(json)!.AsObject();
            Check(root.Count == 4 && root.ContainsKey("payload") && !root.ContainsKey("x") && !root.ContainsKey("unitIds"),
                "Typed " + message.Type + " has exactly one payload and no competing flat fields");
            JsonObject body = root["payload"]!.AsObject();
            string required = message.Type switch
            {
                NetworkMessageType.GotoRequest => "unitIds", NetworkMessageType.GotoCommand => "routes",
                NetworkMessageType.BuildRequest => "position", NetworkMessageType.BuildCommand => "purchasePrice",
                NetworkMessageType.HarvestRequest => "harvesterId", _ => "phase"
            };
            body.Remove(required);
            Check(RejectJson(root.ToJsonString()), "Missing required " + required + " is rejected for " + message.Type);
            root = JsonNode.Parse(json)!.AsObject(); root["payload"]!["unrelatedField"] = 1;
            Check(RejectJson(root.ToJsonString()), "Unknown payload data is rejected for " + message.Type);
            root = JsonNode.Parse(json)!.AsObject(); root["x"] = 20;
            Check(RejectJson(root.ToJsonString()), "Competing flat coordinates are rejected for " + message.Type);
        }
        var gotoRequest = ComplexCommandPayloads.Create(sender, new GotoRequestPayload([unit], target));
        var gotoCommand = ComplexCommandPayloads.Create(hostId, new GotoCommandPayload(sender, [unit], target, routes));
        Check(Replay(gotoRequest).Routes is null, "Client may omit a route for host planning");
        var emptyRoute = ComplexCommandPayloads.Create(hostId, new GotoCommandPayload(sender, [unit], target, [new(unit, [])]));
        Check(Replay(emptyRoute).Routes![0].Cells.Length == 0, "Explicit empty host route remains a valid stop-at-current-position result");
        Check(!ComplexCommandPayloads.TryValidate(gotoCommand with { Routes = null }, out _), "Host cannot omit confirmed routes");
        Check(!ComplexCommandPayloads.TryValidate(gotoCommand with { Routes = [] }, out _), "Every commanded unit needs a route entry");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { UnitIds = [unit, unit] }, out _), "Duplicate unit IDs are rejected");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { Routes = [new(Guid.NewGuid(), [])] }, out _), "Unrelated route recipient is rejected");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { Routes = [new(unit, [], 1, null)] }, out _), "Half-specified route endpoint is rejected");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { Routes = [new(unit, null!)] }, out _), "Null route cells are rejected");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { X = bad }, out _), "Non-finite destination is rejected locally");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { SenderId = Guid.Empty }, out _), "Empty sender is rejected");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { PlayerId = Guid.NewGuid() }, out _), "Request cannot claim a second actor identity");
        Check(!ComplexCommandPayloads.TryValidate(gotoRequest with { PurchasePrice = 1 }, out _), "Cross-command field is rejected locally");
        var buildRequest = ComplexCommandPayloads.Create(sender, new BuildRequestPayload("Reaktor", target, 0));
        Check(Replay(buildRequest).UnitId is null, "Building ID may be allocated by the host when explicitly omitted by a client");
        Check(!ComplexCommandPayloads.TryValidate(buildRequest with { PurchasePrice = 1 }, out _), "Build request cannot choose the host purchase price");
        Check(!ComplexCommandPayloads.TryValidate(buildRequest with { ArmyId = army }, out _), "Build request cannot claim another army");
        var harvest = ComplexCommandPayloads.Create(hostId, new HarvestCommandPayload(unit, HarvestPhase.Harvesting, 0));
        Check(!ComplexCommandPayloads.TryValidate(harvest with { HarvestPhase = (HarvestPhase)999 }, out _), "Undefined harvest phase is rejected");
        Check(!ComplexCommandPayloads.TryValidate(harvest with { CargoAmount = -1 }, out _), "Negative cargo is rejected");
        foreach (Action<JsonObject> corrupt in new Action<JsonObject>[]
        {
            root => root["payload"] = null,
            root => root["payload"]!["target"]!["x"] = "Infinity",
            root => root["payload"]!["target"]!.AsObject().Remove("z"),
            root => root["payload"]!["unitIds"] = null,
            root => root["type"] = "GotoRequest",
            root => root["type"] = (int)NetworkMessageType.HarvestRequest,
            root => root["type"] = (int)NetworkMessageType.StopRequest,
        })
        {
            var root = JsonNode.Parse(Wire(gotoRequest))!.AsObject(); corrupt(root);
            Check(RejectJson(root.ToJsonString()), "Malformed or mismatched typed payload fails before dispatch");
        }
        foreach (string cell in new[] { "null", "[]", "{\"x\":\"bad\",\"y\":2}", "{\"x\":1,\"y\":2,\"z\":3}" })
        {
            JsonObject root = JsonNode.Parse(Wire(gotoCommand))!.AsObject();
            root["payload"]!["routes"]![0]!["cells"]![0] = JsonNode.Parse(cell);
            Check(RejectJson(root.ToJsonString()), "Malformed route cell is rejected as a JSON error, not a transport task crash");
        }
        string duplicate = Wire(gotoRequest).Replace("\"x\":4.5", "\"x\":4.5,\"X\":10");
        Check(RejectJson(duplicate), "Duplicate case-insensitive payload fields are rejected");
        Check(RejectJson(JsonSerializer.Serialize(gotoRequest)), "Old flat v4 command JSON is not silently accepted");
        Check(Replay(new(NetworkMessageType.StopCommand, hostId, UnitIds: [unit])).UnitIds!.Single() == unit,
            "Unmigrated simple commands retain their existing contract");
        using (var local = new NetworkHandler("typed-local"))
        {
            int received = 0, rejected = 0;
            local.MessageReceived += _ => received++;
            local.Diagnostic += _ => rejected++;
            local.ApplyLocalCommand(gotoCommand with { Routes = null });
            local.EnqueueLocalMessage(buildRequest with { ResourceAmount = 1 }); local.Update();
            Check(received == 0 && rejected == 2, "Local and queued invalid commands produce diagnostics and never reach gameplay");
            local.ApplyLocalCommand(gotoCommand);
            Check(received == 1, "Valid local confirmed route uses the same validation contract");
        }

        // Real loopback transport: mismatch rejection, matching client, human/AI gateways, client route replay.
        using var host = new NetworkHandler("typed-host");
        int port = host.CreateSessionAsync("typed-contract-check").GetAwaiter().GetResult();
        void PumpUntil(Func<bool> done, Action? clientTick = null)
        {
            var deadline = Stopwatch.StartNew();
            while (!done() && deadline.Elapsed < TimeSpan.FromSeconds(5))
            { host.Update(); clientTick?.Invoke(); Thread.Sleep(5); }
            if (!done()) throw new Exception("Timed out waiting for loopback typed command check");
        }
        using (var old = new TcpClient())
        {
            old.Connect("127.0.0.1", port);
            byte[] frame = NetworkConnection.Encode(new(NetworkMessageType.JoinSession, Guid.NewGuid(), DisplayName: "old-client", ProtocolVersion: NetworkHandler.ProtocolVersion - 1));
            old.GetStream().Write(frame);
            using var reader = new StreamReader(old.GetStream());
            var response = reader.ReadLineAsync();
            PumpUntil(() => response.IsCompleted);
            var rejected = JsonSerializer.Deserialize<NetworkMessage>(response.GetAwaiter().GetResult()!, NetworkJson.Options)!;
            Check(rejected.Type == NetworkMessageType.JoinRejected && rejected.Error!.Contains("protocol") && host.Members.Count == 0,
                "Actual TCP host rejects older protocol before membership or world synchronization");
        }
        using var client = new NetworkHandler("typed-client");
        client.JoinSessionAsync("127.0.0.1", port).GetAwaiter().GetResult();
        PumpUntil(() => client.Status == NetworkConnectionStatus.Connected, client.Update);
        var receivedRequests = new List<NetworkMessage>();
        host.MessageReceived += message => { if (ComplexCommandPayloads.Handles(message.Type)) receivedRequests.Add(message); };
        var human = new PlayerCommandService(client, client.LocalPeerId);
        human.GotoAsync([unit], new(4.5f, 0, 6.5f)).GetAwaiter().GetResult();
        human.BuildAsync("Reaktor", new(10, 0, 10), 45).GetAwaiter().GetResult();
        human.HarvestAsync(unit, new(5, 0, 5)).GetAwaiter().GetResult();
        PumpUntil(() => receivedRequests.Count == 3, client.Update);
        Check(receivedRequests.Select(m => m.Type).SequenceEqual(new[] { NetworkMessageType.GotoRequest, NetworkMessageType.BuildRequest, NetworkMessageType.HarvestRequest }),
            "Human Goto/Build/Harvest cross actual TCP with typed payloads in order");
        Guid aiId = Guid.NewGuid();
        var ai = new PlayerCommandService(host, aiId);
        ai.GotoAsync([unit], new(4.5f, 0, 6.5f)).GetAwaiter().GetResult();
        ai.BuildAsync("Reaktor", new(10, 0, 10), 45).GetAwaiter().GetResult();
        ai.HarvestAsync(unit, new(5, 0, 5)).GetAwaiter().GetResult();
        host.Update();
        Check(receivedRequests.Count == 6 && receivedRequests.Skip(3).All(m => m.SenderId == aiId && ComplexCommandPayloads.TryValidate(m, out _)),
            "Host AI uses the same three command contracts through the local gateway");
        ai.GotoAsync([], Vector3.Zero).GetAwaiter().GetResult();
        ai.GotoAsync([Guid.Empty], Vector3.Zero).GetAwaiter().GetResult();
        ai.GotoAsync([unit], Vector3.Zero, formationFacingDegrees: float.NaN).GetAwaiter().GetResult();
        host.Update();
        Check(receivedRequests.Count == 6, "Empty selection and invalid facing produce no request or exception at the player gateway");
        ai.GotoAsync([unit, unit], Vector3.Zero).GetAwaiter().GetResult();
        host.Update();
        Check(receivedRequests.Count == 7 && receivedRequests[^1].UnitIds!.SequenceEqual(new[] { unit }),
            "Overlapping selections send one recipient per unit");
        var world = SimulationFixture.World();
        var mobile = new MobileUnit(new(2.5f, 0, 4.5f), unit);
        world.Units.Register(mobile);
        using var input = new NetworkInput(client, world, world.SimulationArmies);
        var confirmed = ComplexCommandPayloads.Create(host.LocalPeerId, new GotoCommandPayload(client.LocalPeerId, [unit], target, routes));
        host.BroadcastAsync(confirmed).GetAwaiter().GetResult();
        PumpUntil(() => mobile.PlannedPath.Count == 2, client.Update);
        Check(mobile.PlannedPath.SequenceEqual(routes[0].Cells) && world.PathfindingManager.PendingRequests == 0,
            "Client executes the exact typed host route without computing a different path");
        return checks;
    }
}
