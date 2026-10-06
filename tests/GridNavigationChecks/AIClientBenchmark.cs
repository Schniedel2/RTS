using RTS;
using RTS.Network;
using Microsoft.Xna.Framework;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
internal static partial class AIReconstructionChecks
{
    public static void RunClientBenchmark(string mode, string directory, int seconds)
    {
        Directory.CreateDirectory(directory);
        bool stress = mode == "stress";
        int remoteCount = mode == "host" ? 0 : mode == "one" ? 1 : 2;
        int peerCount = mode == "split" ? 2 : remoteCount > 0 ? 1 : 0;
        var metrics = new ClientRunDiagnostics();
        Globals.MeshHandler = new MeshHandler();
        Globals.MeshHandler.LoadMeshes(Path.Combine(Environment.CurrentDirectory, "Content", "Models"), loadTextures: false);
        using var scenario = new Scenario(false, 129, metrics);
        foreach (var unit in scenario.World.Units.Units.ToArray()) scenario.Remove(unit);
        scenario.World.Tiberium.ApplyMapStates([]);
        typeof(RTSGame).GetField("<IsMatchStarted>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Globals.Game, true);
        var secondPlayer = new Player(Guid.Parse("c030a67e-e93e-435b-814a-34734830baec"), "Benchmark AI2");
        secondPlayer.SetArmy(Guid.Parse("b030a67e-e93e-435b-814a-34734830baec"));
        var second = new AIPlayer(secondPlayer); scenario.AdditionalAI.Add(second);
        ((List<Player>)typeof(RTSGame).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Globals.Game)!).Add(secondPlayer);
        var secondArmy = scenario.World.SimulationArmies.EnsureArmy(secondPlayer.ArmyId, secondPlayer.Id, Guid.Parse("c130a67e-e93e-435b-814a-34734830baec"));
        var ais = new[] { scenario.AI, second }; var armies = new[] { scenario.Army, secondArmy };
        var profile = AIProfileCatalog.Default.Resolve(AIStrategyProfileType.BalancedAssault, 1829856032);
        var processList = new List<Process>();
        var requests = new Dictionary<string,int>();
        var phaseRequests = new int[3]; var phaseBuilds = new int[3];
        int phase = 0, rejections = 0, completedOrders = 0, disconnects = 0, lateJoins = 0, resets = 0, maxUnits = 0;
        float maxCargo = 0; bool removedReactor = false, removedPerk = false;
        int ownedControllers = 0;
        scenario.Network.SetHostTimeProvider(() => scenario.Host.HostTime);
        var state = new SessionStateService(scenario.World, scenario.World.SimulationArmies);
        scenario.Network.SetSessionSnapshotProvider(() => new(NetworkMessageType.SessionSnapshot, scenario.Network.LocalPeerId,
            SessionSnapshot: state.Capture(scenario.Host.HostTime, true) with { AIControllers = scenario.Network.AIControllers.Snapshot() }));
        var diagnosticsSaved = new List<string>();
        Process Launch(string name, int capacity, bool reconnect)
        {
            string config = Path.Combine(directory, name + ".json"), report = Path.Combine(directory, name + "-metrics.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new BotClientConfig(1, "127.0.0.1", scenario.Network.HostingPort!.Value, name,
                MaximumArmies: capacity, Reconnect: reconnect, DiagnosticsPath: report), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
            info.ArgumentList.Add(Path.Combine(Environment.CurrentDirectory, "bin", "Debug", "net9.0", "RTS.dll")); info.ArgumentList.Add("--bot-client"); info.ArgumentList.Add(config);
            var process = Process.Start(info)!;
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (diagnosticsSaved) diagnosticsSaved.Add(name + ": " + e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (diagnosticsSaved) diagnosticsSaved.Add("ERROR " + name + ": " + e.Data); } };
            process.BeginOutputReadLine(); process.BeginErrorReadLine(); processList.Add(process); return process;
        }
        void Setup()
        {
            for (int index = 0; index < 2; index++)
            {
                var army = armies[index]; var ai = ais[index]; int offset = index * 68;
                army.Resources = 30000;
                Building Building(string type, int x, int z)
                {
                    var building = scenario.World.Units.SpawnBuilding(type, new(x+offset+.5f,0,z+offset+.5f),0,Guid.NewGuid(),ai.Id,armyId:army.Id) ?? throw new Exception("Benchmark placement " + type);
                    building.AdvanceConstruction(building.RemainingBuildingPoints); return building;
                }
                Building("gdi-base",10,10); Building("tiberium-refinery",35,10); Building("gdi-barracks",10,30);
                scenario.World.Units.SpawnUnit("gdi-bulldozer",new(24.5f+offset,0,25.5f+offset),0,Guid.NewGuid(),ai.Id,Guid.NewGuid());
                scenario.World.Units.SpawnUnit("harvester",new(35.5f+offset,0,25.5f+offset),0,Guid.NewGuid(),ai.Id,Guid.NewGuid());
                for (int x=40;x<48;x++) for (int z=40;z<48;z++) scenario.World.Tiberium.ApplySeed(new(x+offset,z+offset,0,100,0,0,1,1,1));
                scenario.Network.AssignAIController(army.Id, ai.Id, scenario.Network.LocalPeerId, profile);
                ai.Controller.BeginMatch(1234, army.Id, profile); ai.SetStatus(AIPlayerStatus.Active);
            }
        }
        Setup();
        scenario.Network.MessageReceived += message => {
            if (message.AIControllerActorId.HasValue && message.Type.ToString().EndsWith("Request"))
            { requests[message.Type.ToString()] = requests.GetValueOrDefault(message.Type.ToString()) + 1; phaseRequests[phase]++; }
            if (message.Type == NetworkMessageType.BuildCommand) phaseBuilds[phase]++;
            if (message.Type == NetworkMessageType.SpawnCommand && message.ProductionOrderId.HasValue || message.Type == NetworkMessageType.ResearchCompletedCommand) completedOrders++;
            if (message.Type == NetworkMessageType.RequestFeedbackCommand && message.RequestFeedback is { } feedback)
            { if (feedback.State == LocalRequestState.Rejected) rejections++; if (feedback.Status == AIOrderStatus.Completed) completedOrders++; }
        };
        for (int i=0;i<peerCount;i++) Launch("BenchBot"+i, mode == "one" || mode == "split" ? 1 : 2, stress);
        var startup = Stopwatch.StartNew();
        while (scenario.Network.Members.Count < peerCount && startup.Elapsed.TotalSeconds < 20) { scenario.Network.Update(); Thread.Sleep(10); }
        if (scenario.Network.Members.Count != peerCount) throw new Exception("Benchmark bot join failed");
        void AssignRemote()
        {
            var peers = scenario.Network.Members.OrderBy(p=>p.DisplayName).ToArray();
            for (int i=0;i<remoteCount;i++)
            { scenario.Network.AssignAIController(armies[i].Id,ais[i].Id,peers[mode=="split"?i:0].Id,profile); ais[i].Controller.BeginMatch(1234,armies[i].Id,profile); }
        }
        AssignRemote();
        // Warm-up excluded from loop quantiles; inclusive scopes remain separately labeled.
        PerformanceMeasurements.Enabled = true; PerformanceMeasurements.Reset();
        var clock = Stopwatch.StartNew(); double previous=0, nextReport=5;
        bool disconnected=false, reassigned=false, lateJoined=false, probe=false;
        try
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                double now=clock.Elapsed.TotalSeconds; long start=Stopwatch.GetTimestamp();
                scenario.Network.Update();
                if (stress && now>=20 && !probe)
                {
                    var gateway=new PlayerCommandService(scenario.Network,ais[0].Id);
                    gateway.StopAsync([scenario.World.Units.Units.First(u=>u.ArmyId==armies[1].Id).UnitId]).GetAwaiter().GetResult();
                    // Wrong-actor token is deliberately rejected before any unit state mutation.
                    for(int n=0;n<3;n++) scenario.Network.Update();
                    if(gateway.LastRequest?.State!=LocalRequestState.Rejected) throw new Exception("Foreign-army probe accepted");
                    probe=true;
                }
                if(stress && now>=30 && !disconnected)
                {
                    var peer=scenario.Network.Members.First(p=>p.DisplayName=="BenchBot0");
                    ((System.Net.Sockets.TcpClient)typeof(NetworkPeer).GetProperty("Client",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(peer)!).Dispose();
                    disconnected=true; disconnects++;
                }
                if(stress && disconnected && !reassigned && now>=38 && scenario.Network.Members.Any(p=>p.DisplayName=="BenchBot0"))
                {
                    if(armies.Any(a=>scenario.Network.AIControllers.Find(a.Id)?.ControllerPeerId!=scenario.Network.LocalPeerId)) throw new Exception("Reconnect not passive");
                    AssignRemote(); reassigned=true;
                }
                if(stress && now>=45 && !lateJoined) { Launch("LateObserver",1,false); lateJoined=true; lateJoins++; }
                if(stress && now>=50 && !removedReactor)
                {
                    var reactor=scenario.World.Units.Units.OfType<Building>().FirstOrDefault(b=>b.GameplayTypeId=="reaktor" && b.ArmyId==armies[0].Id);
                    if(reactor is not null) { var destroy=NetworkCommands.CreateDestroyUnitCommand(scenario.Network.LocalPeerId,reactor.UnitId); scenario.Network.ApplyLocalCommand(destroy); scenario.Network.BroadcastAsync(destroy).GetAwaiter().GetResult(); removedReactor=true; }
                }
                if(stress && now>=55 && !removedPerk)
                {
                    var home=scenario.World.Units.Units.OfType<Building>().FirstOrDefault(b=>b.GameplayTypeId=="gdi-base" && b.ArmyId==armies[1].Id);
                    if(home is not null) { var destroy=NetworkCommands.CreateDestroyUnitCommand(scenario.Network.LocalPeerId,home.UnitId); scenario.Network.ApplyLocalCommand(destroy); scenario.Network.BroadcastAsync(destroy).GetAwaiter().GetResult(); removedPerk=true; }
                }
                if(stress && resets<2 && now>=70+resets*60)
                {
                    if(scenario.World.GameplayMarkers.Markers.Count==0) { scenario.World.GameplayMarkers.Add(GameplayMarkerType.PlayerStart,new(25.5f,0,25.5f),0); scenario.World.GameplayMarkers.Add(GameplayMarkerType.PlayerStart,new(93.5f,0,93.5f),0); }
                    var restart=(NetworkMessage?)typeof(NetworkHost).GetMethod("TryCreateStartMultiplayerGameCommand",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(scenario.Host,[NetworkCommands.CreateStartMultiplayerGameRequest(scenario.Network.LocalPeerId)]) ?? throw new Exception("Restart rejected");
                    resets++; phase=resets;
                    // Use the normal authoritative command and normal replica reconstruction.
                    scenario.Network.ApplyLocalCommand(restart); scenario.Network.BroadcastAsync(restart).GetAwaiter().GetResult(); scenario.Network.Update();
                    for(int i=0;i<2;i++) { scenario.Network.AssignAIController(armies[i].Id,ais[i].Id,scenario.Network.LocalPeerId,profile); ais[i].Controller.BeginMatch(1234,armies[i].Id,profile); }
                    AssignRemote();
                }
                var time=new GameTime(TimeSpan.FromSeconds(now),TimeSpan.FromSeconds(Math.Min(.05,now-previous)));
                scenario.World.SimulationArmies.Update(time);
                scenario.World.Update(time);
                long decision=Stopwatch.GetTimestamp(); foreach(var ai in ais) ai.Update(time,scenario.World,scenario.Network);
                if(now>=5) metrics.Decisions.Add(Stopwatch.GetElapsedTime(decision).TotalMilliseconds);
                scenario.Host.Update(time);
                ownedControllers=ais.Count(ai=>scenario.Network.CanRunAI(ai.Player.ArmyId));
                maxUnits=Math.Max(maxUnits,scenario.World.Units.Units.Count);
                maxCargo=Math.Max(maxCargo,scenario.World.Units.Units.OfType<Harvester>().Select(h=>h.CargoAmount).DefaultIfEmpty().Max());
                if(now>=5) metrics.Frame(Stopwatch.GetElapsedTime(start).TotalMilliseconds,(now-previous)*1000,scenario.Network.PendingMessages,ownedControllers);
                previous=now;
                if(now>=nextReport) { metrics.Save(Path.Combine(directory,"host-metrics.json")); nextReport=now+5; }
                Thread.Sleep(Math.Max(0,16-(int)Stopwatch.GetElapsedTime(start).TotalMilliseconds));
            }
            metrics.Save(Path.Combine(directory,"host-metrics.json"));
            var units=scenario.World.Units.Units;
            var result=new { mode, seconds, profile, requests, phaseRequests, phaseBuilds, maxUnits,maxCargo,rejections=metrics.Rejections,completedOrders,disconnects,lateJoins,resets,removedReactor,removedPerk,
                controllers=scenario.Network.AIControllers.Snapshot(), units=units.GroupBy(u=>u.GameplayTypeId).ToDictionary(g=>g.Key,g=>g.Count()),
                completedBuildings=units.OfType<Building>().Count(b=>b.IsCompleted), pendingPlanning=scenario.World.PathfindingManager.PendingRequests };
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(result));
            if(requests.GetValueOrDefault("BuildRequest")==0 || requests.GetValueOrDefault("TrainUnitRequest")==0 || maxUnits<20) throw new Exception("Insufficient gameplay progress");
            if(stress && (!reassigned || !lateJoined || resets!=2 || !removedReactor || !removedPerk || phaseRequests.Any(n=>n==0) || phaseBuilds.Any(n=>n==0))) throw new Exception("Stress lifecycle/progress incomplete");
        }
        finally
        {
            scenario.Network.Disconnect();
            foreach(var process in processList) { if(!process.WaitForExit(4000)) process.Kill(entireProcessTree:true); process.Dispose(); }
            lock(diagnosticsSaved) File.WriteAllLines(Path.Combine(directory,"bot-logs.txt"),diagnosticsSaved);
        }
        Console.WriteLine("Benchmark passed: "+directory);
    }
}
