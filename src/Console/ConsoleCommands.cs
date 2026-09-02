using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        _rtsGame.NetworkInput.MessageReceived += HandleNetworkMessage;
        _localPlayer.GotoRequested += RequestGoto;
        RegisterCommands();
    }

    private void RegisterCommands()
    {
        _console.RegisterCommand(
            "spawn",
            Spawn);
        _console.RegisterCommand(
            "WhoAmI",
            WhoAmI);
        _console.RegisterCommand(
            "Set",
            Set);
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
        if (!Directory.Exists(batchDirectory))
        {
            _console.Print("No batch files available.");
            return;
        }

        string[] batchFiles = Directory
            .GetFiles(batchDirectory, "*.batch")
            .Select(Path.GetFileName)
            .Where(fileName => fileName is not null)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        if (batchFiles.Length == 0)
        {
            _console.Print("No batch files available.");
            return;
        }

        _console.Print("Batch files:");
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

    private void HandleNetworkMessage(NetworkMessage message)
    {
        if (message.Type == NetworkMessageType.JoinRejected)
        {
            _console.Print($"Session join rejected: {message.Error ?? "Unknown reason"}");
            return;
        }

        if (message.Type == NetworkMessageType.JoinAccepted)
        {
            _console.Print($"Joined session as {_rtsGame.Network.DisplayName}.");
            return;
        }

        if (message.Type == NetworkMessageType.TextMessage)
        {
            if (message.TargetId is not null &&
                message.TargetId != _rtsGame.Network.LocalPeerId)
                return;

            if (!string.IsNullOrWhiteSpace(message.Text))
                _console.Print(message.Text);

            return;
        }

        if (message.Type == NetworkMessageType.SpawnCommand)
        {
            if (message.PlayerId is Guid playerId && message.UnitTypeId is not null)
                SpawnLocally(message.UnitTypeId, playerId, message.UnitId, message.X, message.Y, message.Z);

            return;
        }

        if (message.Type == NetworkMessageType.GotoCommand)
        {
            ExecuteGoto(message);
            return;
        }

    }

    private void RequestGoto(IReadOnlyList<Unit> units, Vector3 target)
    {
        if (!_rtsGame.Network.IsConnected)
            return;

        Guid[] unitIds = units.Select(unit => unit.UnitId).ToArray();
        _ = RequestGotoAsync(unitIds, target);
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

        _ = SendTextMessageAsync(string.Join(' ', args), targetId);
    }

    private async System.Threading.Tasks.Task SendTextMessageAsync(string text, Guid? targetId)
    {
        try
        {
            await _rtsGame.NetworkClient.RequestSayAsync(text, targetId);
        }
        catch (Exception ex)
        {
            _console.Print($"Network error: {ex.Message}");
        }
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

    private void Spawn(string[] args)
    {
        if (args.Length < 1)
        {
            _console.Print("Usage: spawn <unit> [x [y [z]]]");
            return;
        }

        string unitTypeId = args[0].ToLowerInvariant();
        if (unitTypeId is not ("tank" or "soldier" or "car"))
        {
            _console.Print($"Unknown unit: {args[0]}");
            return;
        }

        Vector2 target = _localPlayer.MouseWorldPosition;
        float x = target.X;
        float y = target.Y;
        float z = 0.0f;

        if (args.Length > 1)
            float.TryParse(args[1], out x);
        if (args.Length > 2)
            float.TryParse(args[2], out y);
        if (args.Length > 3)
            float.TryParse(args[3], out z);

        if (!_rtsGame.Network.IsConnected)
        {
            _console.Print("No active network session.");
            return;
        }

        _ = SendSpawnRequestAsync(unitTypeId, x, y, z);
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

    private void SpawnLocally(string unitTypeId, Guid playerId, Guid? unitId, float x, float y, float z)
    {
        Vector3 target = new(x, z, y);
        _world.Markers.ShowGotoMarker(target);

        switch (unitTypeId)
        {
            case "tank":
                _world.Units.SpawnTank(target, unitId);
                break;

            case "soldier":
                _world.Units.SpawnSoldier(target, unitId);
                break;

            case "car":
                _world.Units.SpawnCar(target, unitId);
                break;
        }

        string playerName = _rtsGame.Network.GetPeerDisplayName(playerId);
        _console.Print($"Spawned {unitTypeId} for player {playerName}.");
    }

    private void ExecuteGoto(NetworkMessage message)
    {
        GotoCommand command = new(new Vector2(message.X, message.Z));

        foreach (Guid unitId in message.UnitIds ?? Array.Empty<Guid>())
        {
            Unit? unit = _world.Units.FindById(unitId);
            unit?.TryReceiveGotoCommand(_world, command);
        }

        _world.Markers.ShowGotoMarker(new Vector3(message.X, message.Y, message.Z));
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
            case "markers":
                Globals.Debug_ShowMarkers = enable;
                _console.Print($"Markers {(enable ? "enabled" : "disabled")}.");
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

    private void Set(string[] args)
    {
        if (args.Length == 0)
        {
            _console.Print("current settings: ");
            _console.Print("- DisplayName: " + _rtsGame.Network.DisplayName);                        
        }

        if (args.Length < 2)
        {
            _console.Print("Usage: set displayname <newname>");
            return;
        }

        string varName = args[0].ToLowerInvariant();

        if (varName == "displayname")
            _rtsGame.Network.DisplayName = args[1];
    }
}