using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using RTS.Network;

namespace RTS;

public class ConsoleCommands
{
    private readonly GameConsole _console;
    private readonly GameWorld _world;
    private readonly RTSGame _rtsGame;
    private readonly PlayerHandler _localPlayer;
    private const int MaximumBatchLines = 256;
    private const int MaximumBatchDepth = 8;
    private int _batchDepth;

    public ConsoleCommands(
        GameConsole console,
        RTSGame rtsGame)
    {
        _console = console;
        _rtsGame = rtsGame;
        _world = rtsGame.World;
        _localPlayer = rtsGame.LocalPlayer;

        RegisterCommands();
    }

    private void RegisterCommands()
    {
        _console.RegisterCommand(
            "spawn",
            Spawn);
        _console.RegisterCommand(
            "spawn-neutral",
            SpawnNeutral);
        _console.RegisterCommand(
            "WhoAmI",
            WhoAmI);
        _console.RegisterCommand(
            "telemetry",
            Telemetry);
        _console.RegisterCommand(
            "enable",
            Enable);
        _console.RegisterCommand(
            "disable",
            Disable);
        _console.RegisterAsyncCommand(
            "session-host",
            CreateSession);
        _console.RegisterAsyncCommand(
            "session-join",
            JoinSession);
        _console.RegisterAsyncCommand(
            "session-open",
            OpenSession);
        _console.RegisterCommand(
            "session-members",
            ListMembers);
        _console.RegisterCommand(
            "session-list",
            ListSessions);
        _console.RegisterCommand(
            "say",
            Say);
        _console.RegisterCommand(
            "say-to",
            SayTo);
        _console.RegisterAsyncCommand(
            "call",
            CallBatch);
        _console.RegisterCommand(
            "map-save",
            SaveMap);
        _console.RegisterCommand(
            "map-load",
            LoadMap);
        _console.RegisterCommand(
            "map-list",
            ListMap);
        _console.RegisterCommand(
            "set-player-displayname",
            SetPlayerDisplayName);
        _console.RegisterAsyncCommand(
            "set-player-skin",
            SetPlayerSkinAsync);
        _console.RegisterAsyncCommand(
            "map-publish",
            PublishMapAsync);
        _console.RegisterAsyncCommand(
            "map-pull",
            PullMapAsync);
        _console.RegisterAsyncCommand(
            "editor-start",
            StartEditorAsync);
        _console.RegisterAsyncCommand(
            "editor-end",
            EndEditorAsync);
        _console.RegisterCommand(
            "terrain-hills",
            AddHills);
        _console.RegisterCommand(
            "terrain-mountain",
            AddMountain);
        _console.RegisterCommand(
            "terrain-river",
            AddRiver);
        _console.RegisterCommand(
            "terrain-smooth",
            SmoothTerrain);
        _console.RegisterCommand(
            "terrain-symmetry",
            ImproveTerrainSymmetry);
        _console.RegisterCommand(
            "set-clock",
            SetClock);
        _console.RegisterCommand(
            "set-wind",
            SetWind);
        _console.RegisterCommand(
            "spawn-smokeemitter",
            SpawnSmokeEmitter);
        _console.RegisterAsyncCommand(
            "ai-create",
            CreateAIPlayerAsync);
        _console.RegisterAsyncCommand(
            "ai-spawn",
            SpawnForAIPlayerAsync);
        _console.RegisterCommand(
            "ai-status",
            SetAIPlayerStatus);
        _console.RegisterCommand(
            "ai-list",
            ListAIPlayers);
        _console.RegisterCommand(
            "army-list",
            ListArmies);
        _console.RegisterAsyncCommand(
            "army-share",
            ShareArmyAsync);
        _console.RegisterAsyncCommand(
            "army-unshare",
            UnshareArmyAsync);
        _console.RegisterAsyncCommand(
            "army-gift",
            GiftUnitAsync);
        _console.RegisterAsyncCommand(
            "army-gift-selected",
            GiftSelectedUnitsAsync);
        _console.RegisterAsyncCommand(
            "army-merge",
            MergeArmyAsync);
    }

    public async System.Threading.Tasks.Task CallBatch(string[] args)
    {
        string batchDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "Console",
            "Batches");

        if (args.Length == 0 || args[0] == "?")
        {
            ListBatchFiles(batchDirectory);
            return;
        }

        if (_batchDepth >= MaximumBatchDepth)
        {
            _console.Print($"Batch nesting limit reached ({MaximumBatchDepth}).");
            return;
        }

        string fileName = args[0];
        if (Path.GetFileName(fileName) != fileName)
        {
            _console.Print("Usage: call <name>.batch [args]");
            return;
        }

        if (Path.GetExtension(fileName).Length == 0)
            fileName += ".batch";

        if (!string.Equals(Path.GetExtension(fileName), ".batch", StringComparison.OrdinalIgnoreCase))
        {
            _console.Print("Usage: call <name>.batch [args]");
            return;
        }

        if (!Directory.Exists(batchDirectory))
        {
            _console.Print("Batch file not found: " + fileName);
            return;
        }

        string? batchPath = Directory
            .GetFiles(batchDirectory, "*.batch")
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                fileName,
                StringComparison.OrdinalIgnoreCase));

        if (batchPath is null)
        {
            _console.Print($"Batch file not found: {fileName}");
            return;
        }

        string[] batchArguments = args.Skip(1).ToArray();
        _batchDepth++;

        try
        {
            string[] lines = File.ReadLines(batchPath).Take(MaximumBatchLines + 1).ToArray();
            if (lines.Length > MaximumBatchLines)
            {
                _console.Print($"Batch file exceeds the {MaximumBatchLines}-line limit.");
                return;
            }

            foreach (string line in lines)
            {
                string command = line.Trim();
                if (command.Length == 0 || command.StartsWith('#') || command.StartsWith("//"))
                    continue;

                await _console.ExecuteCommandAsync(
                    ReplaceBatchArguments(command, batchArguments));
            }
        }
        catch (IOException ex)
        {
            _console.Print($"Could not read batch file: {ex.Message}");
        }
        finally
        {
            _batchDepth--;
        }
    }

    private void ListBatchFiles(string batchDirectory)
    {
        string[] batchFiles = IOHelper.GetFiles(batchDirectory, "*.batch");
        _console.Print($"Batch files in {batchDirectory}:");
        foreach (string fileName in batchFiles)
            _console.Print($"  {fileName}");
    }

    private static string ReplaceBatchArguments(string command, string[] arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
            command = command.Replace($"${index + 1}", arguments[index], StringComparison.Ordinal);

        return command;
    }

    private System.Threading.Tasks.Task CreateSession(string[] args)
    {
        if (args.Length != 1)
        {
            _console.Print("Usage: session-host <session-name>");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        return CreateSessionAsync(args[0]);
    }

    private async System.Threading.Tasks.Task CreateSessionAsync(string sessionName)
    {
        try
        {
            int port = await _rtsGame.Network.CreateSessionAsync(sessionName);
            _console.Print($"Session '{_rtsGame.Network.SessionName}' created on port {port}.");
        }
        catch (Exception ex)
        {
            _console.Print($"Session error: {ex.Message}");
        }
    }

    private System.Threading.Tasks.Task JoinSession(string[] args)
    {
        if (args.Length != 1)
        {
            _console.Print("Usage: session-join <session-name>");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        return JoinSessionAsync(args[0]);
    }

    private async System.Threading.Tasks.Task JoinSessionAsync(string sessionName)
    {
        try
        {
            await _rtsGame.Network.JoinSessionAsync(sessionName);
            _console.Print($"Connecting to session '{sessionName}' as {_rtsGame.Network.DisplayName}...");
        }
        catch (Exception ex)
        {
            _console.Print($"Session error: {ex.Message}");
        }
    }

    private System.Threading.Tasks.Task OpenSession(string[] args)
    {
        if (args.Length != 1)
        {
            _console.Print("Usage: session-open <session-name>");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        return OpenSessionAsync(args[0]);
    }

    private async System.Threading.Tasks.Task OpenSessionAsync(string sessionName)
    {
        try
        {
            IReadOnlyList<SessionInfo> sessions = await _rtsGame.Network.DiscoverSessionsAsync(
                TimeSpan.FromSeconds(2));
            SessionInfo? existingSession = sessions.FirstOrDefault(session =>
                string.Equals(session.SessionName, sessionName, StringComparison.OrdinalIgnoreCase));

            if (existingSession is not null)
            {
                await _rtsGame.Network.JoinSessionAsync(sessionName);
                _console.Print($"Opened existing session '{sessionName}' as {_rtsGame.Network.DisplayName}.");
                return;
            }

            int port = await _rtsGame.Network.CreateSessionAsync(sessionName);
            _console.Print($"Session '{_rtsGame.Network.SessionName}' was not found and is now hosted on port {port}.");
        }
        catch (Exception ex)
        {
            _console.Print($"Session open error: {ex.Message}");
        }
    }

    private void ListSessions(string[] args)
    {
        _ = ListSessionsAsync();
    }

    private async System.Threading.Tasks.Task ListSessionsAsync()
    {
        try
        {
            IReadOnlyList<SessionInfo> sessions = await _rtsGame.Network.DiscoverSessionsAsync(
                TimeSpan.FromSeconds(2));

            if (sessions.Count == 0)
            {
                _console.Print("No sessions found.");
                return;
            }

            _console.Print("Sessions:");
            foreach (SessionInfo session in sessions)
                _console.Print($"  {session.SessionName} | {session.Address}:{session.Port} | Host: {session.HostName}");
        }
        catch (Exception ex)
        {
            _console.Print($"Session discovery error: {ex.Message}");
        }
    }

    private void ListMembers(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("Only the session host can list members.");
            return;
        }

        foreach (NetworkPeer member in _rtsGame.Network.Members)
            _console.Print($"{member.Id} {member.DisplayName}");
    }

    private void SendToHost(string[] args)
    {
        if (!TryGetNetworkCommand(args, out string command, out string[] commandArguments))
            return;

        _ = SendToHostAsync(command, commandArguments);
    }

    private async System.Threading.Tasks.Task SendToHostAsync(string command, string[] arguments)
    {
        try
        {
            await _rtsGame.Network.SendCommandToHostAsync(command, arguments);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }

    private void SendToAll(string[] args)
    {
        if (!TryGetNetworkCommand(args, out string command, out string[] commandArguments))
            return;

        _ = SendToAllAsync(command, commandArguments);
    }

    private async System.Threading.Tasks.Task SendToAllAsync(string command, string[] arguments)
    {
        try
        {
            await _rtsGame.Network.SendCommandToAllAsync(command, arguments);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }

    private void SendToMember(string[] args)
    {
        if (args.Length < 2 || !Guid.TryParse(args[0], out Guid memberId))
        {
            _console.Print("Usage: net-member <member-id> <command> [args]");
            return;
        }

        string command = args[1];
        string[] commandArguments = args.Skip(2).ToArray();
        _ = SendToMemberAsync(memberId, command, commandArguments);
    }

    private async System.Threading.Tasks.Task SendToMemberAsync(Guid memberId, string command, string[] arguments)
    {
        try
        {
            await _rtsGame.Network.SendCommandToMemberAsync(memberId, command, arguments);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }

    private void Say(string[] args)
    {
        SendTextMessage(args, null);
    }

    private void SayTo(string[] args)
    {
        if (args.Length < 2 || !Guid.TryParse(args[0], out Guid peerId))
        {
            _console.Print("Usage: say-to <peer-id> <text>");
            return;
        }

        SendTextMessage(args[1..], peerId);
    }

    private void SendTextMessage(string[] args, Guid? targetId)
    {
        if (args.Length == 0)
        {
            _console.Print(targetId is null ? "Usage: say <text>" : "Usage: say-to <peer-id> <text>");
            return;
        }

        if (!_rtsGame.Network.IsConnected)
        {
            _console.Print("No active network session.");
            return;
        }

        _ = _rtsGame.NetworkClient.RequestSayAsync(string.Join(' ', args), targetId);
    }

    private async System.Threading.Tasks.Task RequestGotoAsync(Guid[] unitIds, Vector3 target)
    {
        try
        {
            await _rtsGame.NetworkClient.RequestGotoAsync(
                unitIds,
                target.X,
                target.Y,
                target.Z);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }

    private bool TryGetPort(string[] args, out int port)
    {
        if (args.Length == 1 && int.TryParse(args[0], out port) && port is > 0 and <= 65535)
            return true;

        _console.Print("Usage: session-host <name> <port>");
        port = 0;
        return false;
    }

    private bool TryGetNetworkCommand(string[] args, out string command, out string[] commandArguments)
    {
        if (args.Length > 0)
        {
            command = args[0];
            commandArguments = args.Skip(1).ToArray();
            return true;
        }

        _console.Print("Usage: net-<host|all> <command> [args]");
        command = "";
        commandArguments = Array.Empty<string>();
        return false;
    }

    private async System.Threading.Tasks.Task CreateAIPlayerAsync(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("AI players can only be created by the host.");
            return;
        }
        if (args.Length is < 1 or > 2)
        {
            _console.Print("Usage: ai-create <name> [teamId]");
            return;
        }

        int teamId = 0;
        if (args.Length == 2 && !int.TryParse(args[1], out teamId))
        {
            _console.Print("teamId must be an integer.");
            return;
        }

        try
        {
            AIPlayer aiPlayer = _rtsGame.CreateAIPlayer(args[0], teamId);
            await aiPlayer.Player.RequestUpdateAsync(_rtsGame.NetworkClient);
            _console.Print($"Created AI '{aiPlayer.Name}' id={aiPlayer.Id.ToString("N")[..8]} team={teamId} status={aiPlayer.Status}.");
        }
        catch (Exception ex)
        {
            _console.Print($"Could not create AI: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SpawnForAIPlayerAsync(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("AI units can only be spawned by the host.");
            return;
        }
        if (args.Length is < 2 or > 5)
        {
            _console.Print("Usage: ai-spawn <ai-name|id> <unit> [x [z [y]]]");
            return;
        }

        AIPlayer? aiPlayer = _rtsGame.FindAIPlayer(args[0]);
        if (aiPlayer is null)
        {
            _console.Print($"AI player not found: {args[0]}");
            return;
        }

        Vector3 target = _localPlayer.MouseWorldPosition;
        float x = target.X;
        float y = target.Y;
        float z = target.Z;
        if (args.Length > 2 && !float.TryParse(args[2], out x) ||
            args.Length > 3 && !float.TryParse(args[3], out z) ||
            args.Length > 4 && !float.TryParse(args[4], out y))
        {
            _console.Print("Coordinates must be numbers.");
            return;
        }

        try
        {
            await _rtsGame.NetworkClient.RequestSpawnForPlayerAsync(
                aiPlayer.Id, args[1].ToLowerInvariant(), x, y, z);
            _console.Print($"Spawn request sent: {args[1]} for AI '{aiPlayer.Name}'.");
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }

    private void SetAIPlayerStatus(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("AI status can only be changed by the host.");
            return;
        }
        if (args.Length is < 1 or > 2)
        {
            _console.Print("Usage: ai-status <ai-name|id> [Idle|Active|Building|Gathering]");
            return;
        }

        AIPlayer? aiPlayer = _rtsGame.FindAIPlayer(args[0]);
        if (aiPlayer is null)
        {
            _console.Print($"AI player not found: {args[0]}");
            return;
        }
        if (args.Length == 1)
        {
            _console.Print($"AI '{aiPlayer.Name}' status={aiPlayer.Status}.");
            return;
        }
        if (!Enum.TryParse(args[1], ignoreCase: true, out AIPlayerStatus status))
        {
            _console.Print("Unknown AI status. Use Idle, Active, Building, or Gathering.");
            return;
        }

        aiPlayer.SetStatus(status);
        _console.Print($"AI '{aiPlayer.Name}' status={status}.");
    }

    private void ListAIPlayers(string[] args)
    {
        if (_rtsGame.AIPlayers.Count == 0)
        {
            _console.Print("No AI players.");
            return;
        }

        foreach (AIPlayer aiPlayer in _rtsGame.AIPlayers)
            _console.Print($"AI {aiPlayer.Name} id={aiPlayer.Id.ToString("N")[..8]} team={aiPlayer.Player.TeamId} status={aiPlayer.Status}");
    }

    private void Spawn(string[] args)
    {
        if (args.Length < 1)
        {
            _console.Print("Usage: spawn <unit> [x [z [y]]]");
            return;
        }

        string unitTypeId = args[0].ToLowerInvariant();
        Vector3 target = _localPlayer.MouseWorldPosition;
        float x = target.X;
        float y = target.Y;
        float z = target.Z;

        if (args.Length > 1)
            float.TryParse(args[1], out x);
        if (args.Length > 2)
            float.TryParse(args[2], out z);
        if (args.Length > 3)
            float.TryParse(args[3], out y);

        if (!_rtsGame.Network.IsConnected)
        {
            _console.Print("No active network session.");
            return;
        }

        _ = SendSpawnRequestAsync(unitTypeId, x, y, z);
    }

    private void SpawnNeutral(string[] args)
    {
        if (args.Length < 1)
        {
            _console.Print("Usage: spawn-neutral <unit> [x [z [y]]]");
            return;
        }

        string unitTypeId = args[0].ToLowerInvariant();
        Vector3 target = _localPlayer.MouseWorldPosition;
        float x = target.X;
        float y = target.Y;
        float z = target.Z;

        if (args.Length > 1)
            float.TryParse(args[1], out x);
        if (args.Length > 2)
            float.TryParse(args[2], out z);
        if (args.Length > 3)
            float.TryParse(args[3], out y);

        _ = SendNeutralSpawnRequestAsync(unitTypeId, x, y, z);
    }

    private async System.Threading.Tasks.Task SendNeutralSpawnRequestAsync(
        string unitTypeId,
        float x,
        float y,
        float z)
    {
        try
        {
            await _rtsGame.NetworkClient.RequestNeutralSpawnAsync(unitTypeId, x, y, z);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SendSpawnRequestAsync(string unitTypeId, float x, float y, float z)
    {
        try
        {
            await _rtsGame.NetworkClient.RequestSpawnAsync(unitTypeId, x, y, z);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
    }


    private void Telemetry(string[] args)
    {   
        if (args.Length == 1)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "reset":
                    Globals.Telemetry.Reset();
                    break;
            }
            return;
        }

        _console.Print($"Pathfinding calls: {Globals.Telemetry.TryFindPath_Calls}");
        _console.Print($"- Avg. Pathfinding time (ms): {Globals.Telemetry.Pathfinding_Avg}");
        _console.Print($"- Last Pathfinding time (ms): {Globals.Telemetry.Pathfinding_Last}");
        _console.Print($"- Total Pathfinding time (ms): {Globals.Telemetry.Pathfinding_Total}");
        _console.Print($"Frames processed: {Globals.Telemetry.FramesProcessed}");
        _console.Print($"Tiberium cells: {_world.Tiberium.Cells.Count}");
        _console.Print($"- Render chunks: {_world.Tiberium.RenderChunkCount}");
        _console.Print($"- Visible chunks: {_world.Tiberium.LastVisibleChunkCount}");
        _console.Print($"- Drawn cells: {_world.Tiberium.LastDrawnCellCount}");
    }

    private void Enable(string[] args)
    {
        if (args.Length == 0)
        {
            _console.Print("Usage: enable <feature>");
            return;
        }

        EnableFlag(args[0], true);
    }

    private void Disable(string[] args)
    {
        if (args.Length == 0)
        {
            _console.Print("Usage: disable <feature>");
            return;
        }

        EnableFlag(args[0], false);
    }

    private void EnableFlag(string name, bool enable)
    {
        if (name == "?")
        {
            _console.Print($"Lighting: {!Globals.DebugFX_DisableLighting}");
            _console.Print($"Shadowmap: {!Globals.DebugFX_DisableShadowMap}");
            _console.Print($"Shadowmappreview: {Globals.Debug_ShadowMap_ShowPreview}");
            _console.Print($"Gamegrid: {Globals.Debug_ShowGameGrid}");
            _console.Print($"Unitbounds: {Globals.Debug_ShowUnitBounds}");
            _console.Print($"Unittransforms, transforms, axes: {Globals.Debug_ShowUnitTransforms}");
            _console.Print($"Markers: {Globals.Debug_ShowMarkers}");
            _console.Print($"Network, networkmessages: {Globals.Debug_ShowNetworkMessages}");
            _console.Print($"Path, pathfinding, pathmessages: {Globals.Debug_ShowPathfindingMessages}");
            return;
        }

        switch (name.ToLowerInvariant())
        {
            case "shadowmap":
                Globals.DebugFX_DisableShadowMap = !enable;
                _console.Print($"Shadow map {(enable ? "enabled" : "disabled")}.");
                break;
            case "lighting":
                Globals.DebugFX_DisableLighting = !enable;
                _console.Print($"Lighting {(enable ? "enabled" : "disabled")}.");
                break;
            case "shadowmappreview":
                Globals.Debug_ShadowMap_ShowPreview = enable;
                _console.Print($"Shadow map preview {(enable ? "enabled" : "disabled")}.");
                break;
            case "gamegrid":
                Globals.Debug_ShowGameGrid = enable;
                _console.Print($"Game grid {(enable ? "enabled" : "disabled")}.");
                break;
            case "unitbounds":
                Globals.Debug_ShowUnitBounds = enable;
                _console.Print($"Unit bounds {(enable ? "enabled" : "disabled")}.");
                break;
            case "unittransforms":
            case "transforms":
            case "axes":
                Globals.Debug_ShowUnitTransforms = enable;
                _console.Print($"Unit transforms {(enable ? "enabled" : "disabled")}.");
                break;
            case "markers":
                Globals.Debug_ShowMarkers = enable;
                _console.Print($"Markers {(enable ? "enabled" : "disabled")}.");
                break;
            case "network":
            case "networkmessages":
                Globals.Debug_ShowNetworkMessages = enable;
                _console.Print($"Network message debug {(enable ? "enabled" : "disabled")}.");
                break;
            case "pathfinding":
            case "path":
            case "pathmessages":
                Globals.Debug_ShowPathfindingMessages = enable;
                _console.Print($"Pathfinding debug {(enable ? "enabled" : "disabled")}.");
                break;
            default:
                _console.Print($"Unknown flag: {name}");
                break;
        }
    }

    private void WhoAmI(string[] args)
    {
        _console.Print($"You are: {_rtsGame.Network.DisplayName}");
    }

    private void SetPlayerDisplayName(string[] args)
    {
        if (args.Length == 0)
        {
            _console.Print("current settings: ");
            _console.Print("- DisplayName: " + _rtsGame.Network.DisplayName);                        
            _console.Print("");
            _console.Print("Usage: set-player-displayname <newname>");
            return;
        }

        _rtsGame.Network.DisplayName = args[1];
    }

    private void SaveMap(string[] args)
    {
        string mapName = args[0];
        Globals.World.Save(mapName);
    }

    private void LoadMap(string[] args)
    {
        if (args.Length == 0)
        {
            _console.Print("Usage: load <mapname>");
            return;
        }

        string mapName = args[0];
        Globals.World.Load(mapName);
    }

    private void ListMap(string[] args)
    {
        string[] mapDirectories = IOHelper.GetDirectories(Globals.MapsDirectory);
        _console.Print($"Maps available in {Globals.MapsDirectory}:");
        foreach (string fileName in mapDirectories)
            _console.Print($"  {fileName}");
   }

    private async System.Threading.Tasks.Task SetPlayerSkinAsync(string[] args)
    {
        if (args.Length != 1 || !Globals.SkinHandler.TryParse(args[0], out PlayerSkin skin))
        {
            _console.Print("Usage: set-player-skin <Green|Blue|Gray|Yellow|Pink|Cyan|Violet|Red|Brown|Fancy|Yellow-Brown|Light-Green|Sand|Snow|Safari|Orange|Light-Pink|Light-Orange|Dark-Gray>");
            return;
        }

        Player? localPlayer = _rtsGame.Players.FirstOrDefault(player =>
            player.Id == _rtsGame.Network.LocalPeerId);
        if (localPlayer is null)
        {
            _console.Print("Local player was not found.");
            return;
        }

        try
        {
            await localPlayer.RequestSkinAsync(_rtsGame.NetworkClient, skin);
            _console.Print($"Requested player skin {Globals.SkinHandler.Get(skin).Name}.");
        }
        catch (Exception ex)
        {
            _console.Print($"Player skin error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task PublishMapAsync(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("Only the session host can publish a map.");
            return;
        }

        try
        {
            NetworkMessage map = NetworkCommands.CreateWorldData(
                _rtsGame.Network.LocalPeerId, _world.GetWorldData());
            await _rtsGame.Network.BroadcastAsync(map);
            _console.Print("Map published to all connected clients.");
        }
        catch (Exception ex)
        {
            _console.Print($"Map publish error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task PullMapAsync(string[] args)
    {
        if (_rtsGame.Network.IsHost)
        {
            _console.Print("The host already owns the current map.");
            return;
        }
        if (!_rtsGame.Network.IsConnected)
        {
            _console.Print("No active network session.");
            return;
        }

        try
        {
            await _rtsGame.NetworkClient.RequestWorldDataAsync();
            _console.Print("Map requested from host.");
        }
        catch (Exception ex)
        {
            _console.Print($"Map pull error: {ex.Message}");
        }
    }

    private void AddHills(string[] args)
    {
        int seed = Environment.TickCount;
        if (args.Length is < 3 or > 4 ||
            !TryParseInt(args[0], out int count) ||
            !TryParseFloat(args[1], out float radius) ||
            !TryParseFloat(args[2], out float height) ||
            (args.Length == 4 && !TryParseInt(args[3], out seed)))
        {
            _console.Print("Usage: terrain-hills <count> <radius> <height> [seed]");
            return;
        }

        TerrainGenerator.AddHills(_world.Terrain, count, radius, height, seed);
        _console.Print($"Added {count} hills (seed {seed}).");
    }

    private void AddMountain(string[] args)
    {
        if (args.Length != 4 || !TryParseFloat(args[0], out float x) ||
            !TryParseFloat(args[1], out float z) || !TryParseFloat(args[2], out float radius) ||
            !TryParseFloat(args[3], out float height))
        {
            _console.Print("Usage: terrain-mountain <x> <z> <radius> <height>");
            return;
        }

        TerrainGenerator.AddMountain(_world.Terrain, x, z, radius, height);
        _console.Print("Mountain added.");
    }

    private void AddRiver(string[] args)
    {
        if (args.Length != 6 || !TryParseFloat(args[0], out float startX) ||
            !TryParseFloat(args[1], out float startZ) || !TryParseFloat(args[2], out float endX) ||
            !TryParseFloat(args[3], out float endZ) || !TryParseFloat(args[4], out float width) ||
            !TryParseFloat(args[5], out float depth))
        {
            _console.Print("Usage: terrain-river <startX> <startZ> <endX> <endZ> <width> <depth>");
            return;
        }

        TerrainGenerator.AddRiver(_world.Terrain, new Vector2(startX, startZ), new Vector2(endX, endZ), width, depth);
        _console.Print("River added.");
    }

    private void SmoothTerrain(string[] args)
    {
        if (args.Length != 1 || !TryParseFloat(args[0], out float blend))
        {
            _console.Print("Usage: terrain-smooth <blend 0..1>");
            return;
        }

        TerrainGenerator.Smooth(_world.Terrain, blend);
        _console.Print("Terrain smoothed.");
    }

    private void ImproveTerrainSymmetry(string[] args)
    {
        if (args.Length != 2 || !TryParseFloat(args[1], out float blend) ||
            (args[0] != "horizontal" && args[0] != "vertical"))
        {
            _console.Print("Usage: terrain-symmetry <horizontal|vertical> <blend 0..1>");
            return;
        }

        TerrainGenerator.ImproveSymmetry(_world.Terrain, args[0] == "horizontal", blend);
        _console.Print($"{args[0]} terrain symmetry improved.");
    }

    private async System.Threading.Tasks.Task StartEditorAsync(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("Only the session host can start editor mode.");
            return;
        }
        if (_world.IsEditorActive)
        {
            _console.Print("Editor mode is already active.");
            return;
        }

        Vector3 position = _localPlayer.MouseWorldPosition;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
            !_world.GameGrid.Contains(_world.GameGrid.ToCell(position)))
            position = _world.Center;
        try
        {
            await _rtsGame.NetworkClient.RequestSpawnAsync("editor", position.X, position.Y, position.Z);
            _console.Print("Editor mode requested. Tiberium simulation will pause when the editor unit spawns.");
        }
        catch (Exception ex)
        {
            _console.Print($"Editor start error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task EndEditorAsync(string[] args)
    {
        if (!_rtsGame.Network.IsHost)
        {
            _console.Print("Only the session host can end editor mode.");
            return;
        }

        TerrainEditorTool[] editors = _world.Units.Units.OfType<TerrainEditorTool>().ToArray();
        if (editors.Length == 0)
        {
            _console.Print("Editor mode is not active.");
            return;
        }

        foreach (TerrainEditorTool editor in editors)
        {
            NetworkMessage destroy = NetworkCommands.CreateDestroyUnitCommand(
                _rtsGame.Network.LocalPeerId, editor.UnitId);
            _rtsGame.Network.EnqueueLocalMessage(destroy);
            await _rtsGame.Network.BroadcastAsync(destroy);
        }
        _console.Print($"Editor mode ended; removed {editors.Length} editor unit(s).");
    }

    private void ListArmies(string[] args)
    {
        foreach (Player player in _rtsGame.Players)
            _console.Print($"Player {player.Name} id={player.Id.ToString("N")[..8]} team={player.TeamId} army={player.ArmyId.ToString("N")[..8]}");
        foreach (Army army in _rtsGame.Armies.Armies)
            _console.Print($"Army {army.Id.ToString("N")[..8]} owners={string.Join(',', army.OwnerPlayerIds.Select(id => id.ToString("N")[..8]))} resources={army.Resources}");
    }

    private async System.Threading.Tasks.Task ShareArmyAsync(string[] args) =>
        await SetArmyShareAsync(args, grant: true);

    private async System.Threading.Tasks.Task UnshareArmyAsync(string[] args) =>
        await SetArmyShareAsync(args, grant: false);

    private async System.Threading.Tasks.Task SetArmyShareAsync(string[] args, bool grant)
    {
        if (args.Length != 1 || !TryFindPlayer(args[0], out Player? recipient))
        {
            _console.Print($"Usage: army-{(grant ? "share" : "unshare")} <player-name|id>");
            return;
        }
        Player? local = GetLocalPlayer();
        if (local is null)
            return;
        await _rtsGame.NetworkClient.RequestArmyControlAsync(local.ArmyId, recipient!.Id, grant);
        _console.Print($"Army control {(grant ? "grant" : "revoke")} request sent for {recipient.Name}.");
    }

    private async System.Threading.Tasks.Task GiftUnitAsync(string[] args)
    {
        if (args.Length != 2 || !Guid.TryParse(args[0], out Guid unitId) || !TryFindPlayer(args[1], out Player? recipient))
        {
            _console.Print("Usage: army-gift <unit-guid> <player-name|id>");
            return;
        }
        await _rtsGame.NetworkClient.RequestTransferUnitAsync(unitId, recipient!.Id);
        _console.Print($"Transfer request sent for unit {unitId.ToString("N")[..8]}.");
    }

    private async System.Threading.Tasks.Task GiftSelectedUnitsAsync(string[] args)
    {
        if (args.Length != 1 || !TryFindPlayer(args[0], out Player? recipient))
        {
            _console.Print("Usage: army-gift-selected <player-name|id>");
            return;
        }
        foreach (Unit unit in _localPlayer.SelectedUnits)
            await _rtsGame.NetworkClient.RequestTransferUnitAsync(unit.UnitId, recipient!.Id);
        _console.Print($"Transfer requests sent for {_localPlayer.SelectedUnits.Count} selected unit(s).");
    }

    private async System.Threading.Tasks.Task MergeArmyAsync(string[] args)
    {
        if (args.Length != 1 || !TryFindPlayer(args[0], out Player? other))
        {
            _console.Print("Usage: army-merge <player-name|id>");
            return;
        }
        Player? local = GetLocalPlayer();
        if (local is null)
            return;
        await _rtsGame.NetworkClient.RequestMergeArmiesAsync(local.ArmyId, other!.ArmyId);
        _console.Print($"Army merge request sent for {other.Name}.");
    }

    private Player? GetLocalPlayer()
    {
        Player? local = _rtsGame.Players.FirstOrDefault(player => player.Id == _rtsGame.Network.LocalPeerId);
        if (local is null)
            _console.Print("Local player was not found.");
        return local;
    }

    private bool TryFindPlayer(string nameOrId, out Player? player)
    {
        player = _rtsGame.Players.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
            candidate.Id.ToString("N").StartsWith(nameOrId, StringComparison.OrdinalIgnoreCase));
        if (player is not null)
            return true;
        _console.Print($"Player not found: {nameOrId}");
        return false;
    }

    private void SetClock(string[] args)
    {
        if (args.Length != 1 || !TryParseFloat(args[0], out float hours))
        {
            _console.Print("Usage: set-clock <hours>");
            return;
        }

        _world.Weather.SetClock(hours);
        _console.Print($"Clock set to {_world.Weather.ClockHours:0.##}:00.");
    }

    private void SetWind(string[] args)
    {
        if (args.Length != 2 ||
            !TryParseFloat(args[0], out float angleDegrees) ||
            !TryParseFloat(args[1], out float speed))
        {
            _console.Print("Usage: set-wind <angle-degrees> <speed>");
            return;
        }

        Vector3 direction = Vector3.TransformNormal(
            Vector3.Forward,
            Matrix.CreateRotationY(MathHelper.ToRadians(angleDegrees)));
        Vector3 windVelocity = direction * speed;
        _world.Weather.SetWind(windVelocity);
        _console.Print(
            $"Wind set to {angleDegrees:0.##} degrees at {speed:0.##} " +
            $"(X={windVelocity.X:0.##}, Z={windVelocity.Z:0.##}).");
    }

    /// <summary>Creates a local-only visual smoke emitter; it is not replicated over the network.</summary>
    private void SpawnSmokeEmitter(string[] args)
    {
        if (args.Length is < 2 or > 5 ||
            !TryParseFloat(args[0], out float x) ||
            !TryParseFloat(args[1], out float z))
        {
            _console.Print("Usage: spawn-smokeemitter <x> <z> [y] [lifetime-seconds] [emissions-per-second]");
            return;
        }

        float y = 0.0f;
        float parsedLifetime = 0.0f;
        float? lifetime = null;
        float emissionsPerSecond = 3.0f;
        if ((args.Length >= 3 && !TryParseFloat(args[2], out y)) ||
            (args.Length >= 4 && !TryParseFloat(args[3], out parsedLifetime)) ||
            (args.Length == 5 && !TryParseFloat(args[4], out emissionsPerSecond)))
        {
            _console.Print("Smoke emitter parameters must be numbers.");
            return;
        }

        if (args.Length >= 4)
        {
            if (parsedLifetime < 0.0f)
            {
                _console.Print("Lifetime must be zero or greater; omit it for an endless emitter.");
                return;
            }
            lifetime = parsedLifetime;
        }

        SmokeEmissionSettings settings = SmokeEmissionPresets.HeavyCannon() with
        {
            ParticleCount = 1,
            Intensity = 0.75f,
            WindInfluence = 1.0f
        };
        _world.SmokeEmitters.Create(
            new Vector3(x, y, z),
            settings,
            emissionsPerSecond,
            lifetime,
            Vector3.Up);
        string duration = lifetime is float seconds ? $"{seconds:0.##} seconds" : "endless";
        _console.Print($"Local smoke emitter spawned at {x:0.##}, {y:0.##}, {z:0.##} ({duration}).");
    }

    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static bool TryParsePlayerColor(string[] args, out Color color)
    {
        color = Color.White;
        if (args.Length == 1)
        {
            if (TryParseHexColor(args[0], out color))
                return true;

            return args[0].ToLowerInvariant() switch
            {
                "red" or "rot" => AssignColor(Color.Red, out color),
                "blue" or "blau" => AssignColor(Color.RoyalBlue, out color),
                "green" or "gruen" or "grün" => AssignColor(Color.LimeGreen, out color),
                "yellow" or "gelb" => AssignColor(Color.Yellow, out color),
                "orange" => AssignColor(Color.Orange, out color),
                "purple" or "lila" => AssignColor(Color.MediumPurple, out color),
                "cyan" or "tuerkis" or "türkis" => AssignColor(Color.Cyan, out color),
                "white" or "weiss" or "weiß" => AssignColor(Color.White, out color),
                _ => false
            };
        }

        if (args.Length != 3 || !byte.TryParse(args[0], out byte red) ||
            !byte.TryParse(args[1], out byte green) || !byte.TryParse(args[2], out byte blue))
            return false;

        color = new Color(red, green, blue);
        return true;
    }

    private static bool TryParseHexColor(string value, out Color color)
    {
        color = Color.White;
        string hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return false;

        color = new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static bool AssignColor(Color value, out Color color)
    {
        color = value;
        return true;
    }
}
