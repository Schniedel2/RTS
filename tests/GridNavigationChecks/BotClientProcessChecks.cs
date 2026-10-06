using RTS;
using RTS.Network;
using Microsoft.Xna.Framework;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
internal static partial class AIReconstructionChecks
{
    public static void RunBotHost(string directory, bool reconnect = false, bool localLaunch = false)
    {
        Globals.MeshHandler = new MeshHandler();
        Globals.MeshHandler.LoadMeshes(Path.Combine(Environment.CurrentDirectory, "Content", "Models"), loadTextures: false);
        using var scenario = new Scenario(false);
        foreach (var unit in scenario.World.Units.Units.ToArray()) scenario.Remove(unit);
        scenario.World.Tiberium.ApplyMapStates([]);
        scenario.Army.Resources = 30000;
        scenario.Network.SetHostTimeProvider(() => scenario.Host.HostTime);
        Building AddBuilding(string type, int x, int z)
        {
            var building = scenario.World.Units.SpawnBuilding(type, new(x + 0.5f, 0, z + 0.5f), 0,
                Guid.NewGuid(), scenario.AI.Id, armyId: scenario.Army.Id) ?? throw new Exception("Cannot place " + type);
            building.AdvanceConstruction(building.RemainingBuildingPoints);
            return building;
        }
        AddBuilding("gdi-base", 10, 10);
        AddBuilding("tiberium-refinery", 35, 10);
        AddBuilding("gdi-barracks", 10, 30);
        var builder = scenario.World.Units.SpawnUnit("gdi-bulldozer", new(24.5f, 0, 25.5f), 0, Guid.NewGuid(), scenario.AI.Id, Guid.NewGuid()) ?? throw new Exception("Missing builder");
        var harvester = (Harvester)(scenario.World.Units.SpawnUnit("harvester", new(35.5f, 0, 25.5f), 0, Guid.NewGuid(), scenario.AI.Id, Guid.NewGuid()) ?? throw new Exception("Missing harvester"));
        for (int x = 40; x < 48; x++) for (int z = 40; z < 48; z++) scenario.World.Tiberium.ApplySeed(new(x,z,0,100,0,0,1,1,1));
        var state = new SessionStateService(scenario.World, scenario.World.SimulationArmies);
        scenario.Network.SetSessionSnapshotProvider(() => new(NetworkMessageType.SessionSnapshot, scenario.Network.LocalPeerId,
            SessionSnapshot: state.Capture(scenario.Host.HostTime, true) with { AIControllers = scenario.Network.AIControllers.Snapshot() }));
        int port = ((System.Net.IPEndPoint)((System.Net.Sockets.TcpListener)typeof(NetworkHandler).GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Network)!).LocalEndpoint).Port;
        File.WriteAllText(Path.Combine(directory,"port.txt"), port.ToString());
        var requests = new Dictionary<string,int>();
        scenario.Network.MessageReceived += message => { if (message.AIControllerActorId.HasValue && message.Type.ToString().EndsWith("Request")) requests[message.Type.ToString()] = requests.GetValueOrDefault(message.Type.ToString()) + 1; };
        using var launcher = localLaunch ? new LocalBotProcesses(scenario.Network, Console.WriteLine) : null;
        launcher?.Start(scenario.AI);
        bool assigned = false, disconnected = false, reconnected = false, reassigned = false;
        int passiveRequests = -1; float cargo = 0; double simulation = 0;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 35)
        {
            scenario.Network.Update();
            launcher?.Update();
            if (launcher is not null && !assigned && scenario.Network.AIControllers.Find(scenario.Army.Id)?.ControllerPeerId is Guid controller && controller != scenario.Network.LocalPeerId)
            {
                assigned = true;
                bool duplicateDenied = false;
                try { launcher.Start(scenario.AI); } catch (InvalidOperationException) { duplicateDenied = true; }
                if (!duplicateDenied) throw new Exception("Duplicate local launch accepted");
            }
            if (!assigned && scenario.Network.Members.FirstOrDefault() is { } peer)
            {
                if (peer.BotOffer is not { MaximumArmies: 1, ProposedProfileId: "balanced-assault" }) throw new Exception("Bot offer not transmitted");
                scenario.Network.AssignAIController(scenario.Army.Id, scenario.AI.Id, peer.Id, AIStrategyProfile.Create(1234, scenario.Army.Id));
                bool denied = false;
                try { scenario.Network.AssignAIController(Guid.NewGuid(), Guid.NewGuid(), peer.Id, AIStrategyProfile.Create(1234, scenario.Army.Id)); }
                catch (ArgumentException) { denied = true; }
                if (!denied) throw new Exception("Host accepted a second army beyond bot capacity");
                assigned = true;
            }
            if (reconnect && assigned && !disconnected && clock.Elapsed.TotalSeconds >= 12)
            {
                var departingPeer = scenario.Network.Members.Single();
                ((System.Net.Sockets.TcpClient)typeof(NetworkPeer).GetProperty("Client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(departingPeer)!).Dispose();
                disconnected = true;
            }
            if (disconnected && !reassigned && scenario.Network.Members.Count == 1 &&
                scenario.Network.AIControllers.Find(scenario.Army.Id)?.ControllerPeerId == scenario.Network.LocalPeerId)
            {
                reconnected = true;
                int count = requests.Values.Sum();
                if (passiveRequests < 0) passiveRequests = count;
                if (count != passiveRequests) throw new Exception("Reconnect self-assigned a controller");
                if (clock.Elapsed.TotalSeconds >= 25)
                {
                    var old = scenario.Network.AIControllers.Find(scenario.Army.Id)!;
                    scenario.Network.AssignAIController(scenario.Army.Id, scenario.AI.Id, scenario.Network.Members.Single().Id, old.Profile);
                    reassigned = true;
                }
            }
            simulation += 0.1;
            var time = new GameTime(TimeSpan.FromSeconds(simulation), TimeSpan.FromSeconds(0.1));
            scenario.World.Update(time); scenario.Host.Update(time);
            cargo = Math.Max(cargo, harvester.CargoAmount);
            File.WriteAllText(Path.Combine(directory,"host.json"), JsonSerializer.Serialize(new { assigned, disconnected, reconnected, reassigned, cargo, requests,
                health = scenario.Network.GetAIControllerHealth(scenario.Army.Id),
                buildings = scenario.World.Units.Units.OfType<Building>().Select(b => new { b.GameplayTypeId, b.IsCompleted }).ToArray() }));
            Thread.Sleep(10);
        }
        if (launcher is not null)
        {
            if (!launcher.Stop(scenario.AI.Id) || launcher.Count != 0) throw new Exception("Local bot stop failed");
            for (int i = 0; i < 100 && scenario.Network.AIControllers.Find(scenario.Army.Id)?.ControllerPeerId != scenario.Network.LocalPeerId; i++)
            { scenario.Network.Update(); Thread.Sleep(10); }
            if (scenario.Network.AIControllers.Find(scenario.Army.Id)?.ControllerPeerId != scenario.Network.LocalPeerId) throw new Exception("Local bot stop did not return controller to host");
            Console.WriteLine("Local bot launch, duplicate prevention, assignment, production, stop and host fallback passed.");
        }
        if (reconnect && (!reconnected || !reassigned || scenario.Network.AIControllers.Find(scenario.Army.Id)?.Generation != 3)) throw new Exception("Reconnect/reassignment failed");
        if (!assigned || requests.GetValueOrDefault("BuildRequest") == 0 || requests.GetValueOrDefault("TrainUnitRequest") == 0 || requests.GetValueOrDefault("GotoRequest") == 0)
            throw new Exception("Production bot did not build/produce/move");
    }
}


