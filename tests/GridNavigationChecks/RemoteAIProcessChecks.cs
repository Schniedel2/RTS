using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Xna.Framework;
using RTS;
using RTS.Network;
internal static partial class AIReconstructionChecks
{
    public static void RunRemoteAIPeer(string[] args)
    {
        string role = args[1], directory = Path.GetFullPath(args[2]);
        string mode = args.Length > 3 ? args[3] : "normal";
        bool failoverTest = mode != "normal";
        var vertices = new BoundingBox(new(-0.4f, 0, -0.4f), new(0.4f, 1, 0.4f)).GetCorners()
            .Select(p => new VertexPositionColorNormalTexture(p, Color.White, Vector3.Up, Vector2.Zero)).ToArray();
        var mesh = new Mesh("test", [new SubMesh("body", vertices, [0, 1, 2], Vector3.Zero)]);
        Globals.MeshHandler = new MeshHandler();
        foreach (Match match in Regex.Matches(File.ReadAllText("src/Handlers/MeshHandler.cs"), "Meshes\\[\"([^\"]+)\"\\]"))
            Globals.MeshHandler.Meshes[match.Groups[1].Value] = mesh;
        Globals.Console = (GameConsole)RuntimeHelpers.GetUninitializedObject(typeof(GameConsole));
        typeof(GameConsole).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Globals.Console, new List<string>());
        if (role == "host")
        {
            using var scenario = new Scenario(false);
            scenario.Network.SetHostTimeProvider(() => scenario.Host.HostTime);
            scenario.Army.Resources = 30000;
            scenario.Remove(scenario.Reactor); // Remote bot must rebuild, not just observe a ready economy.
            foreach (var existing in scenario.World.Units.Units.Where(u => u is MobileUnit { BuildRate: > 0 }).ToArray()) scenario.Remove(existing);
            _ = scenario.World.Units.SpawnUnit("gdi-bulldozer", new(18.5f, 0, 18.5f), 0, Guid.NewGuid(), scenario.AI.Id, Guid.NewGuid())!;
            scenario.Remove(scenario.World.Units.Units.OfType<Harvester>().Single());
            var harvester = (Harvester)scenario.World.Units.SpawnUnit("harvester", new(20.5f, 0, 5.5f), 0, Guid.NewGuid(), scenario.AI.Id, Guid.NewGuid())!;
            for (int x = 35; x < 44; x++) for (int z = 35; z < 44; z++) scenario.World.Tiberium.ApplySeed(new(x, z, 0, 100, 0, 0, 1, 1, 1));
            Guid enemyArmy = Guid.NewGuid(); scenario.World.SimulationArmies.EnsureArmy(enemyArmy, Guid.NewGuid());
            var enemy = scenario.Add(new CatalogBuilding("gdi-base", new(42.5f, 0, 20.5f), Guid.NewGuid()), enemyArmy);
            float enemyInitialHealth = enemy.HitPoints;
            int ExploredCount()
            {
                var grid = scenario.World.Visibility.GetGrid(scenario.Army.Id);
                return Enumerable.Range(0, grid.Width * grid.Height).Count(i => grid[new Point(i % grid.Width, i / grid.Width)] != VisibilityState.Unexplored);
            }
            scenario.World.Visibility.Update();
            int initialExplored = ExploredCount();
            scenario.World.Visibility.GetGrid(scenario.Army.Id).Reveal(new(42, 20), 3);
            var state = new SessionStateService(scenario.World, scenario.World.SimulationArmies);
            scenario.Network.SetSessionSnapshotProvider(() => new NetworkMessage(NetworkMessageType.SessionSnapshot, scenario.Network.LocalPeerId,
                SessionSnapshot: state.Capture(scenario.Host.HostTime, true) with { AIControllers = scenario.Network.AIControllers.Snapshot() }));
            var requests = new Dictionary<string, int>();
            int hostRequests = 0, remoteRequests = 0;
            scenario.Network.MessageReceived += m => {
                if (m.AIControllerActorId.HasValue && m.Type.ToString().EndsWith("Request"))
                { if (m.ControllerPeerId == scenario.Network.LocalPeerId) hostRequests++; else remoteRequests++; }
            };
            scenario.Network.MessageReceived += m => { if (m.Type.ToString().EndsWith("Request")) requests[m.Type.ToString()] = requests.GetValueOrDefault(m.Type.ToString()) + 1; };
            // Scenario session uses the normal first available listening port.
            int port = ((System.Net.IPEndPoint)((System.Net.Sockets.TcpListener)typeof(NetworkHandler).GetField("_listener", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scenario.Network)!).LocalEndpoint).Port;
            File.WriteAllText(Path.Combine(directory, "port.txt"), port.ToString());
            Guid? preservedSite = null; bool fallbackSeen = false, constructionFinished = false;
            AIControllerAssignment? initialAssignment = null;
            double simulation = 0; float maxCargo = 0; bool assigned = false; var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < 40 && !File.Exists(Path.Combine(directory, "stop.txt")))
            {
                scenario.Network.Update();
                if (!assigned && scenario.Network.Members.Count > 0)
                {
                    initialAssignment = scenario.Network.AssignAIController(scenario.Army.Id, scenario.AI.Id, scenario.Network.Members.First().Id, AIStrategyProfile.Create(1234, scenario.Army.Id));
                    assigned = true;
                }
                simulation += 0.25; var time = new GameTime(TimeSpan.FromSeconds(simulation), TimeSpan.FromSeconds(0.25));
                scenario.World.Update(time);
                if (failoverTest && assigned) scenario.AI.Update(time, scenario.World, scenario.Network);
                scenario.Host.Update(time);
                if (failoverTest && preservedSite is null && scenario.Barracks.ProductionQueue.Orders.Count > 0 &&
                    scenario.Home.ProductionQueue.Orders.Any(o => o.UnitTypeId == "air-technology") &&
                    scenario.World.Units.Units.OfType<Building>().FirstOrDefault(b => b.GameplayTypeId == "reaktor" && !b.IsCompleted) is Building activeSite)
                {
                    preservedSite = activeSite.UnitId;
                    File.WriteAllText(Path.Combine(directory, "failover-now.txt"), "paid construction, training and research active");
                }
                if (failoverTest && assigned && scenario.Network.AIControllers.Find(scenario.Army.Id)?.ControllerPeerId == scenario.Network.LocalPeerId)
                {
                    fallbackSeen = true;
                    File.WriteAllText(Path.Combine(directory, "fallback.txt"), "host generation active");
                }
                constructionFinished |= preservedSite is Guid kept && scenario.World.Units.FindById(kept) is Building { IsCompleted: true };
                maxCargo = Math.Max(maxCargo, harvester.CargoAmount);
                File.WriteAllText(Path.Combine(directory, "host.json"), JsonSerializer.Serialize(new { assigned, simulation, mode, fallbackSeen, constructionFinished, preservedSite, hostRequests, remoteRequests,
                    fallbackReason = scenario.Network.GetAIControllerHealth(scenario.Army.Id)?.LastFallbackReason,
                    profilePreserved = initialAssignment?.Profile == scenario.Network.AIControllers.Find(scenario.Army.Id)?.Profile,
                    exploredGain = ExploredCount() - initialExplored, maxCargo, harvest = harvester.GetDebugCommandText(), enemyDamage = enemyInitialHealth - enemy.HitPoints, resources = scenario.Army.Resources,
                    requests, units = scenario.World.Units.Units.GroupBy(u => u.GameplayTypeId).ToDictionary(g => g.Key, g => g.Count()) }));
                Thread.Sleep(10);
            }
        }
        else
        {
            using var network = new NetworkHandler("Remote test client");
            var world = new GameWorld(65, 65, 1, graphicsEnabled: false); Globals.World = world;
            var fakeGame = (RTSGame)RuntimeHelpers.GetUninitializedObject(typeof(RTSGame));
            void Set(string property, object value) => typeof(RTSGame).GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fakeGame, value);
            Set("World", world); Set("Armies", world.SimulationArmies); Set("Network", network); Set("IsMatchStarted", true);
            typeof(RTSGame).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(fakeGame, new List<Player>());
            Globals.Game = fakeGame;
            using var input = new NetworkInput(network, world, world.SimulationArmies, new SessionStateService(world, world.SimulationArmies));
            using var runtime = new RemoteAIRuntime();
            network.JoinSessionAsync("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(directory, "port.txt")))).GetAwaiter().GetResult();
            var clock = Stopwatch.StartNew(); double simulation = 0;
            RequestReceipt? invalid = null;
            var feedbacks = new Dictionary<string, int>();
            var seenFeedback = new HashSet<(Guid, AIOrderStatus)>();
            bool scripted = false, interrupted = false, rejoined = false;
            PlayerCommandService? oldGateway = null; RequestReceipt? staleProbe = null;
            while (clock.Elapsed.TotalSeconds < 38 && !File.Exists(Path.Combine(directory, "stop.txt")))
            {
                network.Update(); simulation += 0.25; var time = new GameTime(TimeSpan.FromSeconds(simulation), TimeSpan.FromSeconds(0.25));
                world.Update(time);
                if (failoverTest && !scripted && network.Status == NetworkConnectionStatus.Connected && network.AIControllers.Snapshot().FirstOrDefault(a => a.ControllerPeerId == network.LocalPeerId) is { } binding)
                {
                    oldGateway = new PlayerCommandService(network, binding.ActorId);
                    var barracks = world.Units.GetArmyUnits(binding.ArmyId).OfType<Building>().First(b => b.GameplayTypeId == "gdi-barracks");
                    var home = world.Units.GetArmyUnits(binding.ArmyId).OfType<Building>().First(b => b.GameplayTypeId == "gdi-base");
                    oldGateway.TrainUnitAsync(barracks.UnitId, "gunner").GetAwaiter().GetResult();
                    oldGateway.ResearchAsync(home.UnitId, "air-technology").GetAwaiter().GetResult();
                    scripted = true;
                }
                if (failoverTest && !interrupted && File.Exists(Path.Combine(directory, "failover-now.txt")))
                {
                    interrupted = true;
                    if (mode == "disconnect") { network.Disconnect(); runtime.Update(time, world, network); }
                }
                if (interrupted && File.Exists(Path.Combine(directory, "fallback.txt")))
                {
                    if (mode == "disconnect" && !rejoined)
                    {
                        network.JoinSessionAsync("127.0.0.1", int.Parse(File.ReadAllText(Path.Combine(directory, "port.txt")))).GetAwaiter().GetResult();
                        rejoined = true;
                    }
                    runtime.Update(time, world, network); // Clean old generation without claiming host's new one.
                    if (network.Status == NetworkConnectionStatus.Connected && staleProbe is null && oldGateway is not null)
                    {
                        oldGateway.StopAsync([world.Units.Units.OfType<Harvester>().First().UnitId]).GetAwaiter().GetResult();
                        staleProbe = oldGateway.LastRequest;
                    }
                }
                else if (!interrupted) runtime.Update(time, world, network);
                if (invalid is null && runtime.Players.FirstOrDefault() is AIPlayer running)
                {
                    var gateway = new PlayerCommandService(network, running.Id);
                    gateway.StopAsync([world.Units.Units.First(u => u.ArmyId != running.Player.ArmyId && u.ArmyId.HasValue).UnitId]).GetAwaiter().GetResult();
                    invalid = gateway.LastRequest;
                }
                foreach (var order in runtime.Players.SelectMany(a => a.Controller.OrderQueue?.Orders ?? []))
                    foreach (var status in order.Result.Transitions)
                        if (seenFeedback.Add((order.Receipt.RequestId, status))) feedbacks[status.ToString()] = feedbacks.GetValueOrDefault(status.ToString()) + 1;
                File.WriteAllText(Path.Combine(directory, "client.json"), JsonSerializer.Serialize(new { network.Status, mode, interrupted, rejoined, staleProbe = staleProbe?.State.ToString(), rejectedProbe = invalid?.State.ToString(), feedbacks, controllers = runtime.Players.Select(a => new {
                    goal = a.Controller.Goal.ToString(), decision = a.Controller.LastDecision, profile = a.Controller.StrategyProfile,
                    orders = a.Controller.OrderQueue?.Orders.Select(o => new { type = o.Request.Type.ToString(), state = o.State.ToString(), result = o.Result.Status.ToString(), reason = o.Result.Reason }) }) }));
                Thread.Sleep(10);
            }
        }
    }
}
