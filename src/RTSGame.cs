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
    public GameWorld World { get; }
    public PlayerHandler LocalPlayer => Globals.LocalPlayer;
    private KeyboardState _previousKeyboardState;
    private ConsoleCommands _consoleCommands = null!;
    private ShadowMap _shadowMap = null!;
    public ShadowMap ShadowMap => _shadowMap;
    public NetworkHandler Network { get; }
    public NetworkInput NetworkInput { get; }
    public NetworkHost NetworkHost { get; }
    public NetworkClient NetworkClient { get; }
    public NetworkSyncDiagnostics NetworkSyncDiagnostics { get; }
    public GameHud Hud { get; }
    private readonly List<Player> _players = [];
    public IReadOnlyList<Player> Players => _players;
    private readonly Dictionary<Guid, AIPlayer> _aiPlayers = [];
    public IReadOnlyCollection<AIPlayer> AIPlayers => _aiPlayers.Values;
    private readonly Dictionary<Guid, (Player Actor, ArmyGoalController Controller)> _manualArmyGoals = [];
    private readonly Dictionary<Guid, ScoutingController> _manualArmyScouts = [];
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
        Pricing = new PricingService(Armies, id => World.Units.FindById(id));
        Globals.World = World;
        Globals.LocalPlayer = new PlayerHandler(World, World.Markers);
        _shadowMap = new ShadowMap(4096);
        Globals.Console = new GameConsole();

        Network = new NetworkHandler();
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
        NetworkInput = new NetworkInput(Network);
        NetworkHost = new NetworkHost(Network, NetworkInput, World);
        NetworkSyncDiagnostics = new NetworkSyncDiagnostics(Network, NetworkInput,
            CaptureSessionSnapshot, message => Globals.Console.Print(message));
        Network.SetHostTimeProvider(() => NetworkHost.HostTime);
        NetworkClient = new NetworkClient(Network);
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
        RuntimeUnitSnapshot[] units = World.Units.Units
            .Where(unit => unit is not TiberiumSource && !unit.IsDying &&
                !string.IsNullOrWhiteSpace(GetSnapshotTypeId(unit)))
            .Select(unit => new RuntimeUnitSnapshot(
                GetSnapshotTypeId(unit), unit.UnitId, unit.CreatorPlayerId, unit.ArmyId,
                unit.Position.X, unit.Position.Y, unit.Position.Z, GetYawDegrees(unit.Transform),
                unit.HitPoints, unit.Behavior, unit is Building building ? building.PurchasePrice : 0,
                unit.GetState(), unit.Occupancy?.Occupants
                    .Select(item => new OccupantSnapshot(item.UnitId, item.Role)).ToArray() ?? [],
                unit is Harvester harvester ? harvester.HarvestPhase : null,
                unit is Harvester cargo ? cargo.CargoAmount : 0.0f))
            .ToArray();
        return new SessionSnapshot(World.GetWorldData(), Armies.GetSnapshot(), units,
            World.Visibility.GetSnapshot(), NetworkHost.HostTime);
    }

    internal void ApplySessionSnapshot(SessionSnapshot snapshot)
    {
        Network.AssertGameThread();
        World.Units.ClearForNetworkSnapshot();
        Armies.ApplySnapshot(snapshot.Armies);
        foreach (Player player in _players)
            if (snapshot.Armies.FirstOrDefault(army => army.Owners.Contains(player.Id)) is ArmySnapshot army)
                player.SetArmy(army.Id);
        World.ApplyWorldData(snapshot.World);

        foreach (RuntimeUnitSnapshot state in snapshot.Units
            .OrderBy(unit => IsSnapshotBuilding(unit.TypeId) ? 0 : 1))
        {
            Vector3 position = new(state.X, state.Y, state.Z);
            Unit? unit = IsSnapshotBuilding(state.TypeId)
                ? World.Units.SpawnBuilding(state.TypeId, position, state.RotationDegrees,
                    state.UnitId, state.CreatorPlayerId, state.PurchasePrice)
                : World.Units.SpawnUnit(state.TypeId, position, state.RotationDegrees,
                    state.UnitId, state.CreatorPlayerId);
            if (unit is null) continue;
            unit.SetArmy(state.ArmyId);
            unit.HitPoints = Math.Clamp(state.HitPoints, 0.0f, unit.MaxHitPoints);
            unit.Behavior = state.Behavior;
            unit.ApplyState(state.State);
            if (unit is Harvester harvester && state.HarvestPhase is HarvestPhase phase)
                harvester.ApplyHarvestState(phase, state.CargoAmount);
        }
        foreach (RuntimeUnitSnapshot container in snapshot.Units.Where(unit => unit.Occupants.Length > 0))
            foreach (OccupantSnapshot occupant in container.Occupants)
                World.Units.EmbarkUnit(occupant.UnitId, container.UnitId, occupant.Role);
        World.Visibility.ApplySnapshot(snapshot.Visibility);
        World.ClearTransientEffects();
        Hud.Reset();
        _fogTexture.Reset();
    }

    private static bool IsSnapshotBuilding(string typeId) => typeId is
        "gdi-barracks" or "gdi-base" or "reaktor" or "turret-minigun" or
        "building-1" or "vehicle-factory" or "communicationstower" or
        "command-center" or "helipad" or "silo" or "tiberium-refinery";

    private static string GetSnapshotTypeId(Unit unit) => unit switch
    {
        GenericBuilding => "building-1",
        TerrainEditorTool => "editor",
        Soldier when string.IsNullOrWhiteSpace(unit.GameplayTypeId) => "soldier",
        Car when string.IsNullOrWhiteSpace(unit.GameplayTypeId) => "car",
        _ => unit.GameplayTypeId
    };

    private static float GetYawDegrees(Matrix transform) =>
        MathHelper.ToDegrees(MathF.Atan2(-transform.Forward.X, -transform.Forward.Z));

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

        int aiStrategyMatchSeed = Random.Shared.Next();
        foreach (MatchStartAssignment assignment in assignments.Where(item => item.IsAI))
        {
            if (!_aiPlayers.TryGetValue(assignment.PlayerId, out AIPlayer? aiPlayer))
                continue;

            if (_players.All(player => player.Id != aiPlayer.Id))
                _players.Add(aiPlayer.Player);
            Armies.EnsureArmy(assignment.ArmyId, aiPlayer.Id, GuidUtility.FromInt(aiPlayer.Player.TeamId));
            aiPlayer.BeginMatch(aiStrategyMatchSeed, assignment.ArmyId);
        }
    }

    internal bool ApplyCommandCenterGoal(CommandCenter commandCenter, UnitActionType actionType)
    {
        if (!Network.IsHost || !commandCenter.IsOperational || commandCenter.ArmyId is not Guid armyId ||
            Armies.Find(armyId) is not Army army)
            return false;

        if (actionType == UnitActionType.AIStopGoals)
        {
            if (_manualArmyGoals.Remove(armyId, out var running))
                running.Controller.Stop();
            _manualArmyScouts.Remove(armyId);
            return true;
        }

        Player? actor = _players.FirstOrDefault(player => army.OwnerPlayerIds.Contains(player.Id));
        if (actor is null)
            return false;
        if (actionType == UnitActionType.AIStartScouting)
        {
            ScoutingController scouting = new(World, actor.Id);
            scouting.Start(World.Units.Units.Where(unit => unit.ArmyId == armyId));
            _manualArmyScouts[armyId] = scouting;
            return true;
        }

        AIArmyGoal goal = actionType switch
        {
            UnitActionType.AIStartReactor => AIArmyGoal.BuildReactor,
            UnitActionType.AIStartRefinery => AIArmyGoal.BuildRefinery,
            UnitActionType.AIStartEconomy => AIArmyGoal.EstablishEconomy,
            _ => AIArmyGoal.None
        };
        if (goal == AIArmyGoal.None)
            return false;

        if (!_manualArmyGoals.TryGetValue(armyId, out var runningGoal))
            runningGoal = (actor, new ArmyGoalController());
        runningGoal.Controller.Start(goal);
        _manualArmyGoals[armyId] = runningGoal;
        return true;
    }

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

    public void Update(GameTime gameTime, Camera camera, Viewport viewport)
    {
        Globals.Telemetry.FramesProcessed++;
        Network.Update();
        if (Network.Status is NetworkConnectionStatus.Connecting or NetworkConnectionStatus.Synchronizing)
            return;

        float deltaTime =
            (float)gameTime.ElapsedGameTime.TotalSeconds;

        Armies.Update(gameTime);

        Army? localArmy = TryGetLocalPlayer(out Player hudPlayer)
            ? Armies.Find(hudPlayer.ArmyId) : null;
        bool hudConsumed = Hud.Update(gameTime, camera, viewport,
            LocalPlayer.SelectedUnits, localArmy, World.IsEditorActive, !Globals.Console.IsOpen);
        if (!hudConsumed)
            camera.UpdateMouse(gameTime);
        if (!Globals.Console.IsOpen && !hudConsumed)
            camera.UpdateKeyboard(gameTime);
        camera.UpdateTerrainHeight(gameTime, World.Terrain);
        if (!hudConsumed)
            LocalPlayer.Update(gameTime, camera, viewport);
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
            Globals.FogOfWarEnabled && !World.IsEditorActive ? 1.0f : 0.0f);
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
