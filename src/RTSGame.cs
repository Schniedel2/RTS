using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RTS.Network;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class RTSGame
{
    public LocalBotProcesses LocalBots { get; }
    public LocalDedicatedServer LocalServer { get; }
    public GameWorld World { get; }
    public PlayerHandler LocalPlayer => Globals.LocalPlayer;
    private KeyboardState _previousKeyboardState;
    private ConsoleCommands _consoleCommands = null!;
    private ShadowMap _shadowMap = null!;
    public ShadowMap ShadowMap => _shadowMap;
    public NetworkHandler Network { get; }
    public NetworkInput NetworkInput { get; }
    public SessionStateService SessionState { get; }
    public NetworkHost NetworkHost { get; }
    public NetworkClient NetworkClient { get; }
    public NetworkSyncDiagnostics NetworkSyncDiagnostics { get; }
    public GameHud Hud { get; }
    private readonly List<Player> _players = [];
    public IReadOnlyList<Player> Players => _players;
    private readonly Dictionary<Guid, AIPlayer> _aiPlayers = [];
    public IReadOnlyCollection<AIPlayer> AIPlayers => _aiPlayers.Values;
    public RemoteAIRuntime RemoteAI { get; } = new();
    private readonly Dictionary<Guid, (Player Actor, ArmyGoalController Controller)> _manualArmyGoals = [];
    private readonly Dictionary<Guid, ScoutingController> _manualArmyScouts = [];
    public bool IsMatchStarted { get; private set; }
    /// <summary>
    /// Number of complete simulation steps executed per rendered frame. This is
    /// intended for local AI test matches; each step keeps the original frame
    /// duration so movement and timers retain their normal numerical behavior.
    /// </summary>
    public int SimulationStepsPerFrame { get; internal set; } = 1;
    public float WorkSpeedMultiplier => IsMatchStarted
        ? 1.0f
        : GameplayPacing.PreMatchWorkMultiplier;
    public TeamHandler Teams { get; } = new();
    public ArmyHandler Armies { get; } = new();
    public PricingService Pricing { get; }
    public RemoteSelectionHandler RemoteSelections { get; } = new();
    private readonly FogOfWarTexture _fogTexture;
    private float _fogRefreshElapsed;

    public RTSGame(
        int terrainWidth,
        int terrainHeight)
    {
        Globals.ModelsDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Models");

        Globals.TextureHandler = new TextureHandler(Globals.GraphicsDevice);
        Globals.MaterialMaskTextureHandler = new TextureHandler(Globals.GraphicsDevice);
        Globals.TilemapHandler = new TilemapHandler();
        Globals.SkinHandler = new SkinHandler();
        Globals.SkinHandler.LoadSkinTextures();
        Globals.TilemapHandler.LoadTilemaps();
        Globals.TextureHandler.LoadModelTextures(Globals.ModelsDirectory);

        Globals.MeshHandler = new MeshHandler();
        Globals.MeshHandler.LoadMeshes();

        Globals.Game = this;
        Globals.RenderHelper = new RenderHelper(Globals.GraphicsDevice);
        Globals._debugRenderer = new DebugRenderer();
        Globals._camera = new Camera();
    
        World = new GameWorld(terrainWidth, terrainHeight, 1);

        Globals.World = World;
        Globals.LocalPlayer = new PlayerHandler(World, World.Markers);
        _shadowMap = new ShadowMap(4096);
        Globals.Console = new GameConsole();

        Network = new NetworkHandler();
        World.ConfigureSimulation(Armies, Network, (firstId, secondId) =>
        {
            Army? first = Armies.Find(firstId), second = Armies.Find(secondId);
            Player? firstOwner = first is null ? null : Players.FirstOrDefault(player => first.OwnerPlayerIds.Contains(player.Id));
            Player? secondOwner = second is null ? null : Players.FirstOrDefault(player => second.OwnerPlayerIds.Contains(player.Id));
            return firstOwner is not null && secondOwner is not null && firstOwner.TeamId == secondOwner.TeamId;
        });
        Pricing = World.SimulationPricing;
        Player localPlayer = new(Network.LocalPeerId, Network.DisplayName);
        _players.Add(localPlayer);
        Teams.UpdateMembership(localPlayer.Id, localPlayer.TeamId, localPlayer.TeamId);
        Armies.EnsureArmy(localPlayer.ArmyId, localPlayer.Id, GuidUtility.FromInt(localPlayer.TeamId));
        _fogTexture = new FogOfWarTexture(Globals.GraphicsDevice, World.GameGrid, World.Visibility);
        Hud = new GameHud(Globals.GraphicsDevice, World, Globals.ActionIcons);
        Network.SetPlayerDataProvider(() => _players.Select(player =>
            NetworkCommands.CreatePlayerUpdateCommand(
                Network.LocalPeerId,
                new NetworkMessage(
                    NetworkMessageType.RequestPlayerUpdate,
                    player.Id,
                    PlayerId: player.Id,
                    DisplayName: player.Name,
                    TeamId: player.TeamId),
                player.Skin)));
        Network.SetWorldDataProvider(() => NetworkCommands.CreateWorldData(
            Network.LocalPeerId,
            World.GetWorldData()));
        Network.SetSessionSnapshotProvider(CreateSessionSnapshotCommand);
        Network.Diagnostic += error => Globals.Console.Print($"[Network] {error}");
        SessionState = new SessionStateService(World, Armies);
        NetworkInput = new NetworkInput(Network, World, Armies, SessionState);
        NetworkHost = new NetworkHost(Network, NetworkInput, World, Armies,
            () => Players, () => AIPlayers, NotifyCombatLoss);
        NetworkSyncDiagnostics = new NetworkSyncDiagnostics(Network, NetworkInput,
            CaptureSessionSnapshot, message => Globals.Console.Print(message));
        Network.SetHostTimeProvider(() => NetworkHost.HostTime);
        NetworkClient = new NetworkClient(Network);
        LocalBots = new LocalBotProcesses(Network, message => Globals.Console.Print(message));
        LocalServer = new LocalDedicatedServer(message => Globals.Console.Print(message));
        LocalServer.Ready += port => _ = Network.JoinSessionAsync("127.0.0.1", port);
        _consoleCommands = new ConsoleCommands(Globals.Console, this);

        Globals.CellHighlightEffect = new BasicEffect(Globals.GraphicsDevice)
        {
            VertexColorEnabled = true
        };


        _ = _consoleCommands.CallBatch(new[] { "autorun.batch" });
    }

    public void ResetMatchPresentation(Vector3? localStartPosition)
    {
        World.ClearTransientEffects();
        World.Visibility.Reset();
        Hud.Reset();
        _fogTexture.Reset();
        _fogRefreshElapsed = 0.0f;
        if (localStartPosition is Vector3 position)
            Globals._camera.CenterForMatchStart(position, World.Center);
    }

    private NetworkMessage CreateSessionSnapshotCommand() => new(
        NetworkMessageType.SessionSnapshot, Network.LocalPeerId,
        SessionSnapshot: CaptureSessionSnapshot());

    internal SessionSnapshot CaptureSessionSnapshot()
    {
        Network.AssertGameThread();
        return SessionState.Capture(NetworkHost.HostTime, IsMatchStarted) with { AIControllers = Network.AIControllers.Snapshot() };
    }

    internal void ApplySessionSnapshot(SessionSnapshot snapshot)
    {
        Network.AssertGameThread();
        IsMatchStarted = snapshot.IsMatchStarted;
        SessionState.Apply(snapshot);
        Network.AIControllers.Restore(snapshot.AIControllers ?? []);
        foreach (Player player in _players)
            if (snapshot.Armies.FirstOrDefault(army => army.Owners.Contains(player.Id)) is ArmySnapshot army)
                player.SetArmy(army.Id);
        Hud.Reset();
        _fogTexture.Reset();
    }

    internal void MarkMatchStarted() => IsMatchStarted = true;

    public void UpdatePlayer(Guid playerId, string name, int teamId, PlayerSkin skin)
    {
        Player? player = _players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is null)
        {
            Player added = new(playerId, name, teamId, skin);
            _players.Add(added);
            Teams.UpdateMembership(added.Id, added.TeamId, teamId);
            Armies.EnsureArmy(added.ArmyId, added.Id, GuidUtility.FromInt(added.TeamId));
            return;
        }

        int previousTeamId = player.TeamId;
        player.SetRequestedData(name, teamId, skin);
        Teams.UpdateMembership(playerId, previousTeamId, teamId);
        Armies.EnsureArmy(player.ArmyId, player.Id, GuidUtility.FromInt(player.TeamId));
    }

    public void RemovePlayer(Guid playerId)
    {
        LocalBots?.Stop(playerId);
        Player? player = _players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is not null)
            _players.Remove(player);
        Teams.RemovePlayer(playerId);
        RemoteSelections.RemovePlayer(playerId);
        _aiPlayers.Remove(playerId);
    }

    /// <summary>Creates an AI identity on the host. Call PublishPlayerAsync afterwards.</summary>
    public AIPlayer CreateAIPlayer(string name, int teamId = 0)
    {
        if (!Network.IsHost)
            throw new InvalidOperationException("AI players can only be created by the host.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An AI player needs a name.", nameof(name));
        if (_players.Any(player => string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"A player named '{name}' already exists.", nameof(name));

        PlayerSkin skin = Globals.SkinHandler.Skins
            .Select(definition => definition.Skin)
            .FirstOrDefault(candidate => _players.All(player => player.Skin != candidate));
        if (teamId <= 0)
            teamId = GetNextAvailableTeamId();
        Player player = new(Guid.NewGuid(), name.Trim(), teamId, skin);
        _players.Add(player);
        Teams.UpdateMembership(player.Id, player.TeamId, player.TeamId);
        Armies.EnsureArmy(player.ArmyId, player.Id, GuidUtility.FromInt(player.TeamId));
        AIPlayer aiPlayer = new(player);
        _aiPlayers.Add(player.Id, aiPlayer);
        Network.AssignAIController(player.ArmyId, player.Id, Network.LocalPeerId, AIStrategyProfile.Create(0, player.ArmyId));
        return aiPlayer;
    }

    internal int GetNextAvailableTeamId()
    {
        HashSet<int> occupied = _players.Select(player => player.TeamId)
            .Where(teamId => teamId > 0)
            .ToHashSet();
        int candidate = 1;
        while (occupied.Contains(candidate))
            candidate++;
        return candidate;
    }

    public AIPlayer? FindAIPlayer(string idOrName)
    {
        if (Guid.TryParse(idOrName, out Guid id) && _aiPlayers.TryGetValue(id, out AIPlayer? byId))
            return byId;

        return _aiPlayers.Values.FirstOrDefault(ai =>
            string.Equals(ai.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    internal void NotifyCombatLoss(Unit lostUnit, Unit? attacker)
    {
        if (lostUnit.ArmyId is not Guid armyId)
            return;
        foreach (AIPlayer ai in _aiPlayers.Values.Where(candidate =>
            candidate.Player.ArmyId == armyId))
        {
            ai.Controller.RecordCombatLoss(lostUnit, attacker);
        }
    }

    internal void PrepareAIPlayersForMatch(IEnumerable<MatchStartAssignment> assignments)
    {
        if (!Network.IsHost)
            return;

        foreach (var previous in Network.AIControllers.Snapshot())
            Network.AssignAIController(previous.ArmyId, previous.ActorId, null, previous.Profile);
        int aiStrategyMatchSeed = Random.Shared.Next();
        foreach (MatchStartAssignment assignment in assignments.Where(item => item.IsAI))
        {
            if (!_aiPlayers.TryGetValue(assignment.PlayerId, out AIPlayer? aiPlayer))
                continue;

            if (_players.All(player => player.Id != aiPlayer.Id))
                _players.Add(aiPlayer.Player);
            Armies.EnsureArmy(assignment.ArmyId, aiPlayer.Id, GuidUtility.FromInt(aiPlayer.Player.TeamId));
            var profile = AIStrategyProfile.Create(aiStrategyMatchSeed, assignment.ArmyId);
            Network.AssignAIController(assignment.ArmyId, aiPlayer.Id, Network.LocalPeerId, profile);
            aiPlayer.BeginMatch(aiStrategyMatchSeed, assignment.ArmyId);
        }
    }

    internal bool ApplyCommandCenterGoal(CommandCenter center, UnitActionType action) =>
        ManualArmyGoals.Apply(center, action, World, Network, Armies, _players, _manualArmyGoals, _manualArmyScouts);

    private void UpdateConsole(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();

        // check if the circumflex (^) key is pressed   
        if (keyboard.IsKeyDown(Keys.F12) &&
            !_previousKeyboardState.IsKeyDown(Keys.F12))
        {
            Globals.Console.Toggle();
        }

        if (Globals.Console.IsOpen)
        {
            if (keyboard.IsKeyDown(Keys.Back) &&
                !_previousKeyboardState.IsKeyDown(Keys.Back))
            {
                Globals.Console.Backspace();
            }

            if (keyboard.IsKeyDown(Keys.Delete) &&
                !_previousKeyboardState.IsKeyDown(Keys.Delete))
            {
                Globals.Console.Delete();
            }

            if (keyboard.IsKeyDown(Keys.Enter) &&
                !_previousKeyboardState.IsKeyDown(Keys.Enter))
            {
                Globals.Console.Execute();
            }

            if (keyboard.IsKeyDown(Keys.Up) &&
                !_previousKeyboardState.IsKeyDown(Keys.Up))
            {
                Globals.Console.HistoryUp();
            }

            if (keyboard.IsKeyDown(Keys.Down) &&
                !_previousKeyboardState.IsKeyDown(Keys.Down))
            {
                Globals.Console.HistoryDown();
            }

            if (keyboard.IsKeyDown(Keys.Left) &&
                !_previousKeyboardState.IsKeyDown(Keys.Left))
            {
                Globals.Console.CursorLeft();
            }
            
            if (keyboard.IsKeyDown(Keys.Right) &&
                !_previousKeyboardState.IsKeyDown(Keys.Right))
            {
                Globals.Console.CursorRight();
            }

            if (keyboard.IsKeyDown(Keys.Home) &&
                !_previousKeyboardState.IsKeyDown(Keys.Home))
            {
                Globals.Console.CursorHome();
            }
            
            if (keyboard.IsKeyDown(Keys.End) &&
                !_previousKeyboardState.IsKeyDown(Keys.End))
            {
                Globals.Console.CursorEnd();
            }

        }

        _previousKeyboardState = keyboard;
    }

    public void Update(GameTime gameTime, Camera camera, Viewport viewport, bool inputFocused = true)
    {
        Globals.Telemetry.FramesProcessed++;
        Network.Update();
        LocalBots.Update();
        LocalServer.Update();
        if (Network.Status is NetworkConnectionStatus.Connecting or NetworkConnectionStatus.Synchronizing)
            return;

        float deltaTime =
            (float)gameTime.ElapsedGameTime.TotalSeconds;

        Armies.Update(gameTime);

        Army? localArmy = TryGetLocalPlayer(out Player hudPlayer)
            ? Armies.Find(hudPlayer.ArmyId) : null;
        bool hudConsumed = Hud.Update(gameTime, camera, viewport,
            LocalPlayer.SelectedUnits, localArmy, World.IsEditorActive, inputFocused && !Globals.Console.IsOpen);
        if (!hudConsumed)
            camera.UpdateMouse(gameTime);
        if (!Globals.Console.IsOpen && !hudConsumed)
            camera.UpdateKeyboard(gameTime);
        camera.UpdateTerrainHeight(gameTime, World.Terrain);
        if (!hudConsumed && inputFocused && !Globals.Console.IsOpen)
            LocalPlayer.Update(gameTime, camera, viewport);
        else
            LocalPlayer.SuspendPointerInput();
        World.Update(gameTime);
        if (Network.IsHost)
        {
            foreach (AIPlayer aiPlayer in _aiPlayers.Values.ToArray())
                aiPlayer.Update(gameTime, World, Network);
            foreach (var runningGoal in _manualArmyGoals.Values.ToArray())
                runningGoal.Controller.Update(gameTime, runningGoal.Actor, World, Network);
            foreach (ScoutingController scouting in _manualArmyScouts.Values.ToArray())
                scouting.Update(gameTime);
        }
        RemoteAI.Update(gameTime, World, Network);
        _fogRefreshElapsed += deltaTime;
        if (TryGetLocalPlayer(out Player viewer))
        {
            if (_fogRefreshElapsed >= 0.25f)
            {
                _fogRefreshElapsed %= 0.25f;
                _fogTexture.Update(viewer.ArmyId);
            }
        }
        NetworkHost.Update(gameTime);
        NetworkSyncDiagnostics.Update(gameTime);
        UpdateConsole(gameTime);

        if (!ShadowMap.UpdateForCamera(
                camera,
                viewport,
                World.Terrain,
                World.Weather.SunAngleRadians))
        {
            // The camera can temporarily point outside the map. Keep the
            // previous global behavior as a safe fallback in that situation.
            ShadowMap.Update(
                World.Center,
                World.Terrain.Width * 1.42f,
                World.Weather.SunAngleRadians);
        }

    }

    public void DrawShadow()
    {
        ShadowMap.Begin();

        World.DrawShadow(
            Globals._shadowEffect,
            Matrix.Identity,
            ShadowMap.View,
            ShadowMap.Projection);

        ShadowMap.End();
    }
  
    public void Draw2D(SpriteBatch spriteBatch)
    {
        World.Draw2D(spriteBatch, Globals._camera, Globals.GraphicsDevice.Viewport);
        Army? localArmy = TryGetLocalPlayer(out Player localPlayer)
            ? Armies.Find(localPlayer.ArmyId) : null;
        Hud.Draw(spriteBatch, Globals._camera, localArmy, World);

        if (Globals.Debug_ShadowMap_ShowPreview)
        {
            spriteBatch.Draw(
                ShadowMap.Texture,
                new Rectangle(10, 10, 300, 300),
                Color.White);
        }

        Globals.Console.Draw(spriteBatch);
    }

    public void Draw3D(Camera camera)
    {
        Globals._terrainEffect.Parameters["FogTexture"]?.SetValue(_fogTexture.Texture);
        Globals._terrainEffect.Parameters["FogWidth"]?.SetValue((float)World.GameGrid.Width * World.GameGrid.CellSize);
        Globals._terrainEffect.Parameters["FogHeight"]?.SetValue((float)World.GameGrid.Height * World.GameGrid.CellSize);
        Globals._terrainEffect.Parameters["FogTexelSize"]?.SetValue(new Vector2(
            1.0f / _fogTexture.Texture.Width,
            1.0f / _fogTexture.Texture.Height));
        Globals._terrainEffect.Parameters["FogEdgeSoftness"]?.SetValue(
            Math.Max(0.0f, Globals.FogOfWarEdgeSoftness));
        Globals._terrainEffect.Parameters["FogOfWarEnabled"]?.SetValue(
            Globals.FogOfWarEnabled && !Globals.IsSpectator && !World.IsEditorActive ? 1.0f : 0.0f);
        Globals._terrainEffect.Parameters["HideUnexploredTerrain"]?.SetValue(
            Globals.HideUnexploredWorld && !World.IsEditorActive ? 1.0f : 0.0f);
        World.DrawTerrain(
            Matrix.Identity,
            camera.View,
            camera.Projection,
            ShadowMap);


        World.DrawUnits(
            camera.View,
            camera.Projection,
            ShadowMap,
            Globals._unitEffect);

        LocalPlayer.Draw3D(camera);
        
        if (Globals.Debug_ShowGameGrid)
            Globals._debugRenderer.DrawGameGrid(World, camera.View, camera.Projection);
    }

    private bool TryGetLocalPlayer(out Player player)
    {
        player = _players.FirstOrDefault(candidate => candidate.Id == Network.LocalPeerId)!;
        return player is not null;
    }

}
