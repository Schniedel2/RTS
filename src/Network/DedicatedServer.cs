using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS.Network;
namespace RTS;

/// <summary>Authoritative host composition. All simulation and admin mutations run on its single update thread.</summary>
public sealed class DedicatedServerRuntime : IDisposable
{
    private readonly DedicatedServerConfig _config;
    private readonly List<Player> _players = [];
    private readonly List<AIPlayer> _ai = [];
    private readonly Dictionary<Guid, AIStrategyProfile> _profiles = [];
    private readonly Dictionary<Guid, (Player Actor, ArmyGoalController Controller)> _goals = [];
    private readonly Dictionary<Guid, ScoutingController> _scouts = [];
    private readonly NetworkInput _input;
    public GameWorld World { get; }
    public NetworkHandler Network { get; }
    public NetworkHost Host { get; }
    public SessionStateService State { get; }
    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<AIPlayer> AIPlayers => _ai;
    public bool IsMatchStarted => State.IsMatchStarted;
    public bool StopRequested { get; private set; }
    public int MatchStarts { get; private set; }
    public int SnapshotsServed { get; private set; }
    private bool _autoStarted;
    private double _simulationTime;
    public DedicatedServerRuntime(DedicatedServerConfig config)
    {
        config.Validate(); _config = config;
        Globals.MeshHandler = new MeshHandler();
        Globals.MeshHandler.LoadMeshes(Globals.ModelsDirectory, loadTextures: false);
        World = GameWorld.LoadHeadlessMap(config.MapDirectory);
        Globals.World = World;
        int starts = World.GameplayMarkers.Markers.Count(m => m.Type == GameplayMarkerType.PlayerStart && m.PlayerSlot is not null);
        if (starts < config.MaximumPlayers + (config.AIPlayers?.Length ?? 0))
            throw new InvalidDataException($"Map has {starts} starts; configured slots require {config.MaximumPlayers + (config.AIPlayers?.Length ?? 0)}.");
        State = new(World, World.SimulationArmies);
        World.SetMatchStateProvider(() => State.IsMatchStarted);
        Network = new(config.SessionName) { RunDiagnostics = config.DiagnosticsPath is null ? null : new(), JoinAdmission = Admit };
        _input = new(Network, World, World.SimulationArmies, State);
        Host = new(Network, _input, World, World.SimulationArmies, () => _players, () => _ai, NotifyLoss,
            () => IsMatchStarted, (center, action) => ManualArmyGoals.Apply(center, action, World, Network, World.SimulationArmies, _players, _goals, _scouts));
        Network.SetHostTimeProvider(() => Host.HostTime);
        Network.SetSessionSnapshotProvider(SnapshotMessage);
        Network.SetPlayerDataProvider(() => _players.Select(PlayerMessage).ToArray());
        Network.MessageReceived += OnMessage;
        Network.Diagnostic += message => System.Console.WriteLine("[Network] " + message);
        foreach (var slot in config.AIPlayers ?? [])
        {
            var player = AddPlayer(Guid.NewGuid(), slot.Name.Trim(), slot.Team);
            var ai = new AIPlayer(player); _ai.Add(ai);
            var type = Enum.GetValues<AIStrategyProfileType>().Single(t => AIProfileCatalog.Id(t) == slot.ProfileId);
            _profiles[player.ArmyId] = AIProfileCatalog.Default.Resolve(type, config.MatchSeed);
        }
    }
    private string? Admit(string name, BotControllerOffer? offer)
    {
        if (_players.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return "Name already in use.";
        if (offer is not null) return Network.Members.Count(m => m.BotOffer is not null) >= 32 ? "Bot peer slots full." : null;
        if (!_config.AllowLateJoin && IsMatchStarted) return "Match already started.";
        return _players.Count(p => _ai.All(a => a.Id != p.Id)) >= _config.MaximumPlayers ? "Player slots full." : null;
    }
    private Player AddPlayer(Guid id, string name, int team = 0)
    {
        if (team == 0) { team = 1; while (_players.Any(p => p.TeamId == team)) team++; }
        var skin = Enum.GetValues<PlayerSkin>().FirstOrDefault(s => _players.All(p => p.Skin != s));
        Guid? ownedArmy = World.SimulationArmies.Armies.FirstOrDefault(a => a.OwnerPlayerIds.Contains(id))?.Id;
        var player = new Player(id, name, team, skin, ownedArmy); _players.Add(player);
        World.SimulationArmies.EnsureArmy(player.ArmyId, player.Id, GuidUtility.FromInt(team));
        return player;
    }
    private NetworkMessage PlayerMessage(Player p) => new(NetworkMessageType.PlayerUpdate, Network.LocalPeerId,
        PlayerId: p.Id, DisplayName: p.Name, TeamId: p.TeamId, PlayerSkin: (int)p.Skin);
    private NetworkMessage SnapshotMessage()
    {
        SnapshotsServed++;
        return new(NetworkMessageType.SessionSnapshot, Network.LocalPeerId,
            SessionSnapshot: State.Capture(Host.HostTime, IsMatchStarted) with { AIControllers = Network.AIControllers.Snapshot() });
    }
    private void OnMessage(NetworkMessage message)
    {
        if (message.Type == NetworkMessageType.MemberJoined && Network.Members.FirstOrDefault(p => p.Id == message.SenderId) is { BotOffer: null } peer)
        {
            var player = AddPlayer(peer.Id, peer.DisplayName);
            _ = Network.BroadcastAsync(PlayerMessage(player));
            // Initial snapshot preceded admission into the roster; send this human their army too.
            _ = Network.SendToPeerAsync(peer.Id, SnapshotMessage());
        }
        if (message.Type == NetworkMessageType.PlayerUpdate && message.PlayerId is Guid id && _players.FirstOrDefault(p => p.Id == id) is Player playerUpdate)
        {
            playerUpdate.SetRequestedData(message.DisplayName!, message.TeamId, (PlayerSkin)(message.PlayerSkin ?? 0));
            World.SimulationArmies.EnsureArmy(playerUpdate.ArmyId, id, GuidUtility.FromInt(message.TeamId));
        }
        if (message.Type == NetworkMessageType.MergeArmiesCommand)
            foreach (Player owner in _players)
                if (World.SimulationArmies.Armies.FirstOrDefault(a => a.OwnerPlayerIds.Contains(owner.Id)) is Army owned)
                    owner.SetArmy(owned.Id);
        if (message.Type == NetworkMessageType.MemberLeft)
            _players.RemoveAll(p => p.Id == message.SenderId && _ai.All(a => a.Id != p.Id));
        if (message.Type == NetworkMessageType.StartMultiplayerGameCommand)
        {
            MatchStarts++;
            foreach (var goal in _goals.Values) goal.Controller.Stop();
            _goals.Clear(); _scouts.Clear();
            foreach (var old in Network.AIControllers.Snapshot()) Network.AssignAIController(old.ArmyId, old.ActorId, null, old.Profile);
            foreach (var ai in _ai)
            {
                var profile = _profiles[ai.Player.ArmyId];
                Network.AssignAIController(ai.Player.ArmyId, ai.Id, Network.LocalPeerId, profile);
                ai.Controller.BeginMatch(_config.MatchSeed, ai.Player.ArmyId, profile); ai.SetStatus(AIPlayerStatus.Active);
            }
            System.Console.WriteLine($"Match started: {MatchStarts}, players={_players.Count}");
        }
    }
    private void NotifyLoss(Unit unit, Unit? attacker)
    { foreach (var ai in _ai.Where(a => a.Player.ArmyId == unit.ArmyId)) ai.Controller.RecordCombatLoss(unit, attacker); }
    public void Update(GameTime time)
    {
        long started = Stopwatch.GetTimestamp(); Network.Update();
        if (!_autoStarted && _config.StartMode == "when-ready" && _players.Count(p => _ai.All(a => a.Id != p.Id)) >= _config.RequiredPlayers)
        { StartMatch(); _autoStarted = true; }
        double elapsed = IsMatchStarted ? time.ElapsedGameTime.TotalSeconds : 0;
        _simulationTime += elapsed;
        time = new GameTime(TimeSpan.FromSeconds(_simulationTime), TimeSpan.FromSeconds(elapsed));
        World.SimulationArmies.Update(time); World.Update(time);
        long decision = Stopwatch.GetTimestamp(); foreach (var ai in _ai) ai.Update(time, World, Network);
        Network.RunDiagnostics?.Decisions.Add(Stopwatch.GetElapsedTime(decision).TotalMilliseconds);
        foreach (var goal in _goals.Values.ToArray()) goal.Controller.Update(time, goal.Actor, World, Network);
        foreach (var scout in _scouts.Values.ToArray()) scout.Update(time);
        Host.Update(time);
        Network.RunDiagnostics?.Frame(Stopwatch.GetElapsedTime(started).TotalMilliseconds, time.ElapsedGameTime.TotalMilliseconds,
            Network.PendingMessages, _ai.Count(a => Network.CanRunAI(a.Player.ArmyId)));
    }
    public void StartMatch()
    {
        Host.EnsureSessionGeneration();
        var command = Host.TryCreateStartMultiplayerGameCommand(NetworkCommands.CreateStartMultiplayerGameRequest(Network.LocalPeerId))
            ?? throw new InvalidOperationException("Cannot start match; check players/start markers.");
        // Same ordering as graphical game-start: publish map before the authoritative reset.
        Network.BroadcastAsync(NetworkCommands.CreateWorldData(Network.LocalPeerId, World.GetWorldData())).GetAwaiter().GetResult();
        Network.ApplyLocalCommand(command);
        Network.BroadcastAsync(command).GetAwaiter().GetResult();
        Network.Update();
    }
    public void Admin(string line)
    {
        var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (args.Length == 0) return;
        switch (args[0].ToLowerInvariant())
        {
            case "start" or "restart" when args.Length == 1: StartMatch(); _autoStarted = true; break;
            case "stop" when args.Length == 1: StopRequested = true; break;
            case "status" when args.Length == 1: SaveStatus(verbose: true); break;
            case "assign" when args.Length == 3:
                var ai = _ai.FirstOrDefault(a => (a.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase) || a.Id.ToString().Equals(args[1], StringComparison.OrdinalIgnoreCase))) ?? throw new ArgumentException("Unknown AI.");
                Guid controller = args[2].Equals("host", StringComparison.OrdinalIgnoreCase) ? Network.LocalPeerId :
                    Network.Members.FirstOrDefault(p => (p.DisplayName.Equals(args[2], StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Equals(args[2], StringComparison.OrdinalIgnoreCase)))?.Id ?? throw new ArgumentException("Unknown peer.");
                if (!IsMatchStarted) throw new InvalidOperationException("Start match before assigning a bot.");
                Network.AssignAIController(ai.Player.ArmyId, ai.Id, controller, _profiles[ai.Player.ArmyId]); break;
            default: throw new ArgumentException("Commands: start, restart, status, assign <AI> <peer|host>, stop");
        }
    }
    public void SaveStatus(bool stopped = false, bool verbose = false)
    {
        var data = new { port = Network.HostingPort, IsMatchStarted, MatchStarts, SnapshotsServed, stopped,
            players = _players.Select(p => new { p.Id, p.Name, p.ArmyId, p.TeamId, isAI = _ai.Any(a => a.Id == p.Id) }).ToArray(),
            peers = Network.Members.Select(p => new { p.Id, p.DisplayName, isBot = p.BotOffer is not null }).ToArray(),
            controllers = Network.AIControllers.Snapshot(), armies = World.SimulationArmies.GetSnapshot(),
            units = World.Units.GetSnapshot().Select(u => new { u.UnitId, type = u.GameplayTypeId, u.ArmyId, x = u.Position.X, y = u.Position.Y, z = u.Position.Z, u.HitPoints,
                completed = u is Building b && b.IsCompleted }).ToArray(), hostTime = Host.HostTime };
        if (_config.StatusPath is string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data));
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Status is diagnostic output. Keep the previous complete file and retry next report.
                System.Console.Error.WriteLine($"Status report deferred ({path}): {error.Message}");
            }
        }
        if (verbose)
        {
            foreach (var player in _players) System.Console.WriteLine($"Player: {player.Name} id={player.Id} army={player.ArmyId} team={player.TeamId}");
            foreach (var peer in Network.Members) System.Console.WriteLine($"Peer: {peer.DisplayName} id={peer.Id} bot={peer.BotOffer is not null}");
        }
        System.Console.WriteLine($"Status: started={IsMatchStarted}, players={_players.Count}, peers={Network.Members.Count}, units={World.Units.Units.Count}");
    }
    public void Dispose()
    {
        foreach (var goal in _goals.Values) goal.Controller.Stop();
        Network.MessageReceived -= OnMessage; _input.Dispose(); Network.Dispose();
    }
}

public static class DedicatedServer
{
    public static int Run(string path)
    {
        try
        {
            var config = DedicatedServerConfig.Load(path);
            if (config.DiagnosticsPath is not null) { PerformanceMeasurements.Enabled = true; PerformanceMeasurements.Reset(); }
            using var server = new DedicatedServerRuntime(config);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            System.Console.CancelKeyPress += cancel;
            var commands = new ConcurrentQueue<string>();
            // Input threads enqueue text only. No world/network mutation occurs here.
            _ = Task.Run(() => { string? line; while ((line = System.Console.ReadLine()) is not null) commands.Enqueue(line); });
            int controlLines = config.ControlFile is string control && File.Exists(control) ? File.ReadAllLines(control).Length : 0;
            try
            {
                int port = server.Network.CreateSessionAsync(config.SessionName, cancellation.Token, config.AutoSelectPort ? null : config.Port).GetAwaiter().GetResult();
                System.Console.WriteLine($"Dedicated server ready: port={port}, map={config.MapDirectory}"); server.SaveStatus();
                var clock = Stopwatch.StartNew(); double previous = 0, nextReport = 5, nextControl = 0;
                while (!cancellation.IsCancellationRequested && !server.StopRequested)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    if (config.ControlFile is string file && now >= nextControl)
                    {
                        nextControl = now + .25;
                        try { if (File.Exists(file)) { string[] lines = File.ReadAllLines(file); if (lines.Length < controlLines) controlLines = 0;
                            foreach (string line in lines.Skip(controlLines)) commands.Enqueue(line); controlLines = lines.Length; } }
                        catch (IOException) { /* Writer may be replacing the local control file; retry next update. */ }
                    }
                    for (int count = 0; count < 32 && commands.TryDequeue(out var command); count++)
                        try { server.Admin(command); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { System.Console.WriteLine("Admin rejected: " + error.Message); }
                    server.Update(new GameTime(TimeSpan.FromSeconds(now), TimeSpan.FromSeconds(Math.Min(.1, now - previous)))); previous = now;
                    if (now >= nextReport) { server.SaveStatus(); if (config.DiagnosticsPath is string report) server.Network.RunDiagnostics!.Save(report); nextReport = now + 5; }
                    Thread.Sleep(16);
                }
                server.SaveStatus(stopped: true); if (config.DiagnosticsPath is string final) server.Network.RunDiagnostics!.Save(final);
                return 0;
            }
            finally { System.Console.CancelKeyPress -= cancel; }
        }
        catch (Exception error) { System.Console.Error.WriteLine("Dedicated server: " + error); return 1; }
    }
}
