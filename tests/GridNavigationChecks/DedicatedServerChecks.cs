using RTS;
using RTS.Network;
using Microsoft.Xna.Framework;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
internal static class DedicatedServerChecks
{
    private static string ServerAssembly => Path.Combine(Environment.CurrentDirectory,"bin",
#if DEBUG
        "Debug",
#else
        "Release",
#endif
        "net9.0","RTS.dll");
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string text){if(!value)throw new Exception("Dedicated server: "+text);checks++;}
        var config=new DedicatedServerConfig(1,"map",MaximumPlayers:2,AIPlayers:[new("AI1")]);
        config.Validate();Check(true,"valid default config");
        foreach(var invalid in new[]{config with {SchemaVersion=2}, config with {MapDirectory=""},config with {Port=0},config with {Port=65536},
            config with {MaximumPlayers=-1},config with {RequiredPlayers=3},config with {StartMode="always"},config with {ControlFile=" "},
            config with {AIPlayers=[new("AI", "missing")]},config with {AIPlayers=[new("AI"),new("ai")]},
            config with {AIPlayers=[new("AI",Team:-1)]},config with {RequiredPlayers=0,AIPlayers=[]},
            config with {ControlFile="same",StatusPath="same"}})
        {bool rejected=false;try{invalid.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid field rejected");}
        string dir=Path.Combine(Path.GetTempPath(),"RTS-server-unit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string file=Path.Combine(dir,"server.json");
            File.WriteAllText(file,JsonSerializer.Serialize(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            Check(DedicatedServerConfig.Load(file).MapDirectory==Path.Combine(dir,"map"),"paths relative to config");
            foreach(string invalid in new[]{"null","{}","[]","{\"schemaVersion\":1,\"schemaVersion\":1}","{\"schemaVersion\":1,\"mapDirectory\":\"map\",\"unknown\":true}"})
            {File.WriteAllText(file,invalid);bool rejected=false;try{DedicatedServerConfig.Load(file);}catch(InvalidDataException){rejected=true;}Check(rejected,"strict JSON contract");}
            WriteMap(Path.Combine(dir,"map"));
            RTSGame savedGame=Globals.Game;GameWorld savedWorld=Globals.World;MeshHandler savedMeshes=Globals.MeshHandler;
            try
            {
                Globals.Game=null!;
                var runtimeConfig=new DedicatedServerConfig(1,Path.Combine(dir,"map"),StatusPath:Path.Combine(dir,"runtime-status.json"),MaximumPlayers:2,RequiredPlayers:0,StartMode:"manual",AIPlayers:[new("AI1"),new("AI2")]);
                using var runtime=new DedicatedServerRuntime(runtimeConfig);
                var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();int fixedPort=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
                runtime.Network.CreateSessionAsync("ServerChecks",port:fixedPort).GetAwaiter().GetResult();
                Check(runtime.Network.HostingPort==fixedPort,"fixed port honored");
                using var blocked=new NetworkHandler("Blocked");bool portRejected=false;
                try{blocked.CreateSessionAsync("Blocked",port:fixedPort).GetAwaiter().GetResult();}catch(InvalidOperationException){portRejected=true;}
                Check(portRejected,"occupied fixed port has no silent fallback");
                var before=JsonSerializer.Serialize(runtime.World.Tiberium.GetStates());
                runtime.Update(new GameTime(TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(10)));
                Check(runtime.Host.HostTime==0 && before==JsonSerializer.Serialize(runtime.World.Tiberium.GetStates()),"lobby freezes simulation and resource growth");
                Check(Globals.Game is null && runtime.World.Units.Units.Any(u=>u is TiberiumSource),"real map imports without game or GPU");
                runtime.SaveStatus();
                string previousStatus=File.ReadAllText(runtimeConfig.StatusPath!);
                using (var held=new FileStream(runtimeConfig.StatusPath!,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    runtime.StartMatch();
                    runtime.SaveStatus();
                    Check(File.ReadAllText(runtimeConfig.StatusPath!)==previousStatus,"locked status file retains previous complete report");
                    runtime.Update(new GameTime(TimeSpan.FromSeconds(.1),TimeSpan.FromSeconds(.1)));
                    Check(runtime.IsMatchStarted && runtime.Host.HostTime>0,"simulation continues despite blocked status replacement");
                }
                runtime.SaveStatus();
                using (var report=JsonDocument.Parse(File.ReadAllText(runtimeConfig.StatusPath!)))
                    Check(report.RootElement.GetProperty("IsMatchStarted").GetBoolean(),"status publication recovers after lock is released");
                var workers=runtime.World.Units.Units.OfType<GDIBulldozer>().Select(u=>u.UnitId).ToArray();
                Check(workers.Length==2 && runtime.World.SimulationArmies.Armies.All(a=>a.Resources==10000),"AI start assignments and resources");
                Check(runtime.Players.Count==2 && runtime.Players.All(p=>p.Id!=runtime.Network.LocalPeerId),"server peer consumes no player slot");
                runtime.StartMatch();
                Check(runtime.MatchStarts==2 && runtime.World.Units.Units.OfType<GDIBulldozer>().All(u=>!workers.Contains(u.UnitId)),"restart replaces units");
                Check(runtime.Network.AIControllers.Snapshot().All(a=>a.Generation==3 && a.Profile.Seed==1234),"restart retains configured profile and seed with new generation");
                var add=typeof(DedicatedServerRuntime).GetMethod("AddPlayer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var first=(Player)add.Invoke(runtime,[Guid.NewGuid(),"First",5])!;
                var second=(Player)add.Invoke(runtime,[Guid.NewGuid(),"Second",5])!;
                Guid mergedId=Guid.NewGuid();
                runtime.Network.ApplyLocalCommand(NetworkCommands.CreateMergeArmiesCommand(runtime.Network.LocalPeerId,
                    new(NetworkMessageType.MergeArmiesRequest,first.Id,ArmyId:first.ArmyId,SecondaryArmyId:second.ArmyId,TargetId:mergedId)));
                runtime.Network.Update();
                Check(first.ArmyId==mergedId && second.ArmyId==mergedId,"dedicated roster follows authoritative army merge");
                runtime.Network.ApplyLocalCommand(new(NetworkMessageType.MemberLeft,first.Id));runtime.Network.Update();
                var rejoined=(Player)add.Invoke(runtime,[first.Id,"First",5])!;
                Check(rejoined.ArmyId==mergedId,"rejoining identity resumes its current army");
                var actor=Guid.NewGuid();var mergedArmy=Guid.NewGuid();
                runtime.World.SimulationArmies.EnsureArmy(mergedArmy,actor,GuidUtility.FromInt(1));
                using var input=new NetworkInput(runtime.Network,runtime.World,runtime.World.SimulationArmies,subscribe:false);
                input.OnMessageReceived(new(NetworkMessageType.PlayerUpdate,runtime.Network.LocalPeerId,PlayerId:actor,DisplayName:"Joined",TeamId:7));
                Check(runtime.World.SimulationArmies.Find(mergedArmy)!.TeamId==GuidUtility.FromInt(7) && runtime.World.SimulationArmies.Find(actor) is null,"headless player updates retain merged army identity");
                input.OnMessageReceived(NetworkCommands.CreateNotifyUnitsSelected(actor,[workers[0]],1));
                input.OnMessageReceived(NetworkCommands.CreateNotifyUnitsSelected(actor,[],2));
                Check(Globals.Game is null,"headless selection and deselection do not require graphical game");
            }
            finally{Globals.Game=savedGame;Globals.World=savedWorld;Globals.MeshHandler=savedMeshes;}
            var logs = new List<string>();
            using (var launcher = new LocalDedicatedServer(logs.Add, ServerAssembly))
            {
                var socket = new TcpListener(IPAddress.Loopback,0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
                bool ready = false; launcher.Ready += actual => ready = actual == port;
                launcher.Start(new DedicatedServerConfig(1, Path.Combine(dir,"map"), Port:port, MaximumPlayers:1,
                    RequiredPlayers:0, StartMode:"manual", AIPlayers:[new("AI1")]));
                bool duplicate = false; try { launcher.Start(config); } catch(InvalidOperationException) { duplicate=true; }
                Check(duplicate,"launcher prevents duplicate child");
                void Wait(Func<bool> complete)
                {
                    var clock=Stopwatch.StartNew();
                    while(!complete() && clock.Elapsed.TotalSeconds<25) { launcher.Update(); Thread.Sleep(20); }
                    Check(complete(),"launcher process progress: " + string.Join(" | ",logs.TakeLast(5)));
                }
                Wait(()=>ready);
                using var human = new NetworkHandler("LauncherHuman");
                human.JoinSessionAsync("127.0.0.1",port).GetAwaiter().GetResult();
                Wait(()=> { human.Update(); return human.Status==NetworkConnectionStatus.Connected; });
                launcher.Admin("start");
                Wait(()=> { using var status=JsonDocument.Parse(launcher.ReadStatus()!); return status.RootElement.GetProperty("IsMatchStarted").GetBoolean(); });
                Guid workerId; float workerX, workerZ;
                using (var status=JsonDocument.Parse(launcher.ReadStatus()!))
                {
                    var worker=status.RootElement.GetProperty("units").EnumerateArray().First(u=>u.GetProperty("type").GetString()=="gdi-bulldozer" && u.GetProperty("ArmyId").GetGuid()==human.LocalPeerId);
                    workerId=worker.GetProperty("UnitId").GetGuid(); workerX=worker.GetProperty("x").GetSingle(); workerZ=worker.GetProperty("z").GetSingle();
                }
                // Match the UI sequence; TCP ordering ensures selection arrives before the build request.
                new NetworkClient(human).NotifyUnitsSelectedAsync([workerId]).GetAwaiter().GetResult();
                var playerCommands=new PlayerCommandService(human,human.LocalPeerId);
                playerCommands.BuildAndConstructAsync("gdi-base",new Vector3(workerX<64?workerX+16:workerX-16,0,workerZ),0,[workerId]).GetAwaiter().GetResult();
                Wait(()=> { human.Update(); using var status=JsonDocument.Parse(launcher.ReadStatus()!);
                    var worker=status.RootElement.GetProperty("units").EnumerateArray().First(u=>u.GetProperty("UnitId").GetGuid()==workerId);
                    return Math.Abs(worker.GetProperty("x").GetSingle()-workerX)+Math.Abs(worker.GetProperty("z").GetSingle()-workerZ)>1; });
                Check(launcher.IsRunning,"selecting human unit then building does not terminate headless host");
                launcher.StartBot("AI1");
                Wait(()=> { using var status=JsonDocument.Parse(launcher.ReadStatus()!); return status.RootElement.GetProperty("peers").EnumerateArray().Any(p=>p.GetProperty("isBot").GetBoolean()); });
                // The normal status output confirms the remote assignment after the game-thread launcher submits it.
                Wait(()=> { using var status=JsonDocument.Parse(launcher.ReadStatus()!);
                    var peers=status.RootElement.GetProperty("peers").EnumerateArray().Where(p=>p.GetProperty("isBot").GetBoolean()).Select(p=>p.GetProperty("Id").GetGuid()).ToArray();
                    return status.RootElement.GetProperty("controllers").EnumerateArray().Any(c=>peers.Contains(c.GetProperty("ControllerPeerId").GetGuid())); });
                launcher.StopAsync().GetAwaiter().GetResult();
                Check(!launcher.IsRunning && launcher.ReadStatus() is null,"launcher stops owned processes and removes private files");
                Check(File.Exists(launcher.LogPath) && File.ReadAllText(launcher.LogPath!).Contains("Dedicated server ready"),"server output survives cleanup");
                File.Delete(launcher.LogPath!);
            }
            // Keep a real listener on the first discovery port while the child binds its own next port.
            var occupied = new TcpListener(IPAddress.Any, NetworkHandler.SessionPortStart);
            bool ownsPort = false;
            try
            {
                try { occupied.Start(); ownsPort = true; } catch(SocketException) { } // An existing listener also exercises the collision.
                using var launcher = new LocalDedicatedServer(_ => {}, ServerAssembly);
                bool ready = false; int actualPort = 0;
                launcher.Ready += actual => { actualPort = actual; ready = true; };
                launcher.Start(new DedicatedServerConfig(1, Path.Combine(dir,"map"), MaximumPlayers:1,
                    RequiredPlayers:0, StartMode:"manual", AutoSelectPort:true));
                var clock = Stopwatch.StartNew();
                while(!ready && launcher.IsRunning && clock.Elapsed.TotalSeconds<25) { launcher.Update(); Thread.Sleep(20); }
                Check(ready && actualPort > NetworkHandler.SessionPortStart && launcher.Port == actualPort,"automatic server port skips occupied 27000 and reports actual port");
                using var human = new NetworkHandler("AutoPortHuman");
                human.JoinSessionAsync("127.0.0.1",launcher.Port).GetAwaiter().GetResult();
                clock.Restart();
                while(human.Status!=NetworkConnectionStatus.Connected && clock.Elapsed.TotalSeconds<10) { human.Update(); launcher.Update(); Thread.Sleep(20); }
                Check(human.Status==NetworkConnectionStatus.Connected,"client connects using child-selected port");
                launcher.StopAsync().GetAwaiter().GetResult();
            }
            finally { if(ownsPort) occupied.Stop(); }
            var failedLogs = new List<string>();
            using (var launcher = new LocalDedicatedServer(failedLogs.Add,ServerAssembly))
            {
                launcher.Start(new DedicatedServerConfig(1,Path.Combine(dir,"missing-map")));
                var clock=Stopwatch.StartNew();
                while(launcher.IsRunning && clock.Elapsed.TotalSeconds<25) { launcher.Update(); Thread.Sleep(20); }
                launcher.Update();
                Check(!launcher.IsRunning && failedLogs.Any(line=>line.Contains("Server exited (1)")),"failed server reports process exit");
                Check(File.Exists(launcher.LogPath) && File.ReadAllText(launcher.LogPath!).Contains("Dedicated server:"),"failure log retains stack trace after process cleanup");
                File.Delete(launcher.LogPath!);
            }
            var terrain=new Terrain(Path.Combine(dir,"map"),graphicsEnabled:false);
            Check(terrain.Width==129 && terrain.Height==129 && terrain.GetHeight(30,30)==0 && terrain.GetTile(30,30)==TerrainTile.Rock,"CPU PNG dimensions/color/height");
        }
        finally {foreach(string file in Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories)) File.Delete(file);foreach(string sub in Directory.EnumerateDirectories(dir))Directory.Delete(sub);Directory.Delete(dir);}
        return checks;
    }
    public static void WriteMap(string directory)
    {
        Directory.CreateDirectory(directory);
        new Terrain(129,129,graphicsEnabled:false).Save(directory);
        var markers=new GameplayMarkerHandler();
        foreach(var point in new[]{new Vector3(22.5f,0,22.5f),new Vector3(94.5f,0,94.5f),new Vector3(22.5f,0,94.5f),new Vector3(94.5f,0,22.5f)})markers.Add(GameplayMarkerType.PlayerStart,point,0);
        markers.Save(directory);
        File.WriteAllText(Path.Combine(directory,"map-objects.json"),JsonSerializer.Serialize(new[]{new MapObjectState(Guid.NewGuid(),"tiberium-source",60.5f,0,60.5f)}));
        var cells=new List<TiberiumSeedState>();for(int x=45;x<55;x++)for(int z=45;z<55;z++)cells.Add(new(x,z,0,100,0,0,1,1,1));
        File.WriteAllText(Path.Combine(directory,TiberiumHandler.FileName),JsonSerializer.Serialize(cells));
    }

    public static void RunProcess(string directory,int seconds, string? existingMap = null)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
        if(seconds<180)throw new ArgumentException("Use at least 180 seconds.");
        string map=existingMap is null ? Path.Combine(directory,"map") : Path.GetFullPath(existingMap); if(existingMap is null) WriteMap(map);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        string control=Path.Combine(directory,"control.txt"),status=Path.Combine(directory,"server-status.json");File.WriteAllText(control,"");
        var config=new DedicatedServerConfig(1,map,port,MaximumPlayers:2,RequiredPlayers:1,AIPlayers:[new("AI1"),new("AI2","fast-recon")],
            ControlFile:control,StatusPath:status,DiagnosticsPath:Path.Combine(directory,"server-metrics.json"));
        string configFile=Path.Combine(directory,"server.json");File.WriteAllText(configFile,JsonSerializer.Serialize(config,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var processes=new List<Process>();var logs=new List<string>();
        Process Launch(string role,string argument,string name)
        {
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=directory};
            start.ArgumentList.Add(ServerAssembly);start.ArgumentList.Add(role);start.ArgumentList.Add(argument);
            var peer=Process.Start(start)!;processes.Add(peer);
            peer.OutputDataReceived+=(_,e)=>{if(e.Data is not null)lock(logs)logs.Add(name+": "+e.Data);};
            peer.ErrorDataReceived+=(_,e)=>{if(e.Data is not null)lock(logs)logs.Add("ERROR "+name+": "+e.Data);};
            peer.BeginOutputReadLine();peer.BeginErrorReadLine();return peer;
        }
        void Admin(string command)=>File.AppendAllText(control,command+Environment.NewLine);
        var server=Launch("--dedicated-server",configFile,"Server");
        using var human=new NetworkHandler("HumanProbe");using var observer=new NetworkHandler("LateHuman");using var overflow=new NetworkHandler("Overflow");
        Globals.MeshHandler=new MeshHandler();Globals.MeshHandler.LoadMeshes(Globals.ModelsDirectory,loadTextures:false);
        GameWorld? world=null;NetworkInput? input=null;SessionStateService? state=null;
        var phaseBuilds=new int[3];int phase=0,snapshots=0,starts=0,lateSnapshots=0,lateStarts=0,maxUnits=0;
        Guid? hostPeer=null;
        bool remoteObserved=false;
        Guid? ownWorker=null;var phasesSent=new HashSet<int>();var accepted=new HashSet<Guid>();var moves=new HashSet<Guid>();
        human.MessageReceived+=message=>{
            if(message.Type==NetworkMessageType.JoinAccepted)hostPeer=message.SenderId;
            if(message.Type==NetworkMessageType.SessionSnapshot && message.SessionSnapshot is {} snapshot)
            {
                snapshots++;input?.Dispose();world=new(snapshot.World.Width,snapshot.World.Height,1,graphicsEnabled:false);Globals.World=world;
                state=new(world,world.SimulationArmies);world.SetMatchStateProvider(()=>state.IsMatchStarted);input=new(human,world,world.SimulationArmies,state,subscribe:false);
            }
            input?.OnMessageReceived(message);
            if(message.Type==NetworkMessageType.StartMultiplayerGameCommand){starts++;phase=Math.Min(2,starts-1);ownWorker=message.MatchStartAssignments?.FirstOrDefault(a=>a.PlayerId==human.LocalPeerId)?.BulldozerId;}
            if(message.Type==NetworkMessageType.BuildCommand)phaseBuilds[phase]++;
            if(message.Type==NetworkMessageType.GotoCommand && message.UnitIds?.Contains(ownWorker??Guid.Empty)==true)moves.Add(ownWorker!.Value);
        };
        observer.MessageReceived+=message=>{if(message.Type==NetworkMessageType.SessionSnapshot)lateSnapshots++;if(message.Type==NetworkMessageType.StartMultiplayerGameCommand)lateStarts++;};
        bool overflowAttempted=false,overflowRejected=false,lateArmyKnown=false;
        overflow.MessageReceived+=m=>{if(m.Type==NetworkMessageType.JoinRejected)overflowRejected=true;};
        RequestReceipt? foreign=null;RequestReceipt? humanPurchase=null;Process? bot=null;
        bool assigned=false,disconnected=false,fallback=false,rejoined=false,reassigned=false,late=false,requestUpdated=false,stopped=false;
        int resets=0;var clock=Stopwatch.StartNew();double previous=0;
        try
        {
            while(!File.Exists(status) && clock.Elapsed.TotalSeconds<20 && !server.HasExited)Thread.Sleep(50);
            if(!File.Exists(status))throw new Exception("Server not ready: "+string.Join("\n",logs));
            human.JoinSessionAsync("127.0.0.1",port).GetAwaiter().GetResult();
            clock.Restart();
            while(clock.Elapsed.TotalSeconds<seconds)
            {
                double now=clock.Elapsed.TotalSeconds;human.Update();observer.Update();overflow.Update();
                if(server.HasExited)throw new Exception("Server exited: "+string.Join("\n",logs));
                if(human.Status==NetworkConnectionStatus.Connected && !requestUpdated)
                {new NetworkClient(human).RequestPlayerUpdateAsync(new(human.LocalPeerId,"HumanProbe")).GetAwaiter().GetResult();requestUpdated=true;}
                if(world is not null)
                {
                    world.Update(new GameTime(TimeSpan.FromSeconds(now),TimeSpan.FromSeconds(Math.Min(.1,now-previous))));maxUnits=Math.Max(maxUnits,world.Units.Units.Count);
                    if(ownWorker is Guid workerId && world.Units.FindById(workerId) is MobileUnit worker && !phasesSent.Contains(phase))
                    {
                        var commands=new PlayerCommandService(human,human.LocalPeerId);
                        commands.GotoAsync([workerId],worker.Position+new Vector3(0,0,5)).GetAwaiter().GetResult();
                        foreach(var delta in new[]{new Vector3(9,0,0),new Vector3(-9,0,0),new Vector3(0,0,9),new Vector3(0,0,-9)})
                        {
                            Vector3 position=worker.Position+delta;
                            var preview=BuildingFactory.SpawnBuilding("gdi-base",position,0,Guid.NewGuid(),human.LocalPeerId);preview!.SetArmy(human.LocalPeerId);
                            if(!preview.EvaluatePlacement(world,position,0).IsAllowed)continue;
                            Guid id=commands.BuildAndConstructAsync("gdi-base",position,0,[workerId]).GetAwaiter().GetResult();
                            humanPurchase=commands.LastRequest;accepted.Add(id);phasesSent.Add(phase);break;
                        }
                    }
                }
                if(now>=20 && bot is null)
                {
                    string file=Path.Combine(directory,"bot.json");File.WriteAllText(file,JsonSerializer.Serialize(new BotClientConfig(1,"127.0.0.1",port,"RemoteBot",MaximumArmies:1,Reconnect:false,DiagnosticsPath:Path.Combine(directory,"bot-metrics.json")),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
                    bot=Launch("--bot-client",file,"Bot");
                }
                if(now>=27 && !assigned){Admin("assign AI1 RemoteBot");assigned=true;}
                if(now>=42 && !late){observer.JoinSessionAsync("127.0.0.1",port).GetAwaiter().GetResult();late=true;}
                if(now>=48 && !overflowAttempted){overflow.JoinSessionAsync("127.0.0.1",port).GetAwaiter().GetResult();overflowAttempted=true;}
                if(now>=52 && world?.SimulationArmies.Find(observer.LocalPeerId) is not null)lateArmyKnown=true;
                if(now>=55 && foreign is null && world?.Units.Units.FirstOrDefault(u=>u.ArmyId!=human.LocalPeerId && u.ArmyId is not null) is Unit enemy)
                {var command=new PlayerCommandService(human,human.LocalPeerId);command.StopAsync([enemy.UnitId]).GetAwaiter().GetResult();foreign=command.LastRequest;}
                if(now>=35 && now<65 && human.AIControllers.Snapshot().Any(a=>a.ControllerPeerId!=hostPeer))remoteObserved=true;
                if(now>=65 && !disconnected){bot!.Kill(entireProcessTree:true);bot.WaitForExit();disconnected=true;}
                if(now>=73 && !fallback)
                {
                    var leases=human.AIControllers.Snapshot();
                    fallback=leases.Length==2 && leases.All(a=>a.ControllerPeerId==hostPeer);
                }
                if(now>=78 && !rejoined){bot=Launch("--bot-client",Path.Combine(directory,"bot.json"),"BotRejoin");rejoined=true;}
                if(now>=86 && !reassigned){Admin("assign AI1 RemoteBot");reassigned=true;}
                if(resets<2 && now>=100+resets*45){Admin("restart");resets++;}
                previous=now;Thread.Sleep(16);
            }
            Admin("status");Thread.Sleep(300);
            using var final=JsonDocument.Parse(File.ReadAllText(status));
            var root=final.RootElement;
            if(!overflowRejected || !lateArmyKnown || starts!=3 || lateStarts!=2 || lateSnapshots<1 || snapshots<2 || phasesSent.Count!=3 || phaseBuilds.Any(n=>n==0) || maxUnits<20 || foreign?.State!=LocalRequestState.Rejected || humanPurchase?.State!=LocalRequestState.Accepted)
                throw new Exception($"Insufficient lifecycle/progress starts={starts} late={lateStarts}/{lateSnapshots} snapshots={snapshots} phases={phasesSent.Count} builds={string.Join(',',phaseBuilds)} units={maxUnits} rejected={foreign?.State} purchase={humanPurchase?.State}");
            if(!remoteObserved || !fallback || !root.GetProperty("controllers").EnumerateArray().All(c=>c.GetProperty("Generation").GetInt64()>=4))throw new Exception("Controller fallback/generations missing");
            if(root.GetProperty("players").GetArrayLength()!=4 || root.GetProperty("MatchStarts").GetInt32()!=3)throw new Exception("Roster reset lost participants");
            var units=root.GetProperty("units").EnumerateArray().ToArray();
            if(!units.Any(u=>u.GetProperty("type").GetString()=="tiberium-source") || !units.Any(u=>u.GetProperty("type").GetString()=="gdi-base" && u.GetProperty("completed").GetBoolean()))throw new Exception("Map objects or actual construction missing");
            File.WriteAllText(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{seconds,starts,lateStarts,lateSnapshots,snapshots,phaseBuilds,maxUnits,fallback,resets,humanBuilds=accepted.Count,foreignRejected=true,moves=moves.Count,remoteObserved,overflowRejected,lateArmyKnown}));
            Admin("stop");if(!server.WaitForExit(10000) || server.ExitCode!=0)throw new Exception("Controlled server stop failed");stopped=true;
            using var end=JsonDocument.Parse(File.ReadAllText(status));if(!end.RootElement.GetProperty("stopped").GetBoolean())throw new Exception("Missing final status");
        }
        finally
        {
            human.Disconnect();observer.Disconnect();overflow.Disconnect();input?.Dispose();
            if(!stopped && !server.HasExited){Admin("stop");if(!server.WaitForExit(4000))server.Kill(entireProcessTree:true);}
            foreach(var process in processes){if(!process.WaitForExit(4000))process.Kill(entireProcessTree:true);process.Dispose();}
            lock(logs)File.WriteAllLines(Path.Combine(directory,"process-logs.txt"),logs);
        }
        if(logs.Any(l=>l.StartsWith("ERROR ") || l.Contains("Admin rejected:")))throw new Exception("Process/admin errors: "+string.Join("\n",logs.Where(l=>l.StartsWith("ERROR ") || l.Contains("Admin rejected:"))));
        Console.WriteLine("Dedicated server process scenario passed: "+directory);
    }
}


