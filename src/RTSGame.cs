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
    public ActionPanel? ActionPanel { get; }
    private readonly List<Player> _players = [];
    public IReadOnlyList<Player> Players => _players;
    private readonly Dictionary<Guid, AIPlayer> _aiPlayers = [];
    public IReadOnlyCollection<AIPlayer> AIPlayers => _aiPlayers.Values;

    public RTSGame(
        int terrainWidth,
        int terrainHeight)
    {
        Globals.ModelsDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Models");

        Globals.TextureHandler = new TextureHandler(Globals.GraphicsDevice);
        Globals.TilemapHandler = new TilemapHandler();
        Globals.SkinHandler = new SkinHandler();
        Globals.SkinHandler.LoadSkinTextures();
        Globals.TilemapHandler.LoadTilemaps();

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

        ActionPanel = new ActionPanel(Globals.ActionIcons);

        Network = new NetworkHandler();
        _players.Add(new Player(Network.LocalPeerId, Network.DisplayName));
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
            World.Terrain.GetWorldData()));
        NetworkInput = new NetworkInput(Network);
        NetworkHost = new NetworkHost(Network, NetworkInput, World);
        NetworkClient = new NetworkClient(Network);
        _consoleCommands = new ConsoleCommands(Globals.Console, this);

        Globals.CellHighlightEffect = new BasicEffect(Globals.GraphicsDevice)
        {
            VertexColorEnabled = true
        };


        _ = _consoleCommands.CallBatch(new[] { "autorun.batch" });
    }

    public void UpdatePlayer(Guid playerId, string name, int teamId, PlayerSkin skin)
    {
        Player? player = _players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is null)
        {
            _players.Add(new Player(playerId, name, teamId, skin));
            return;
        }

        player.SetRequestedData(name, teamId, skin);
    }

    public void RemovePlayer(Guid playerId)
    {
        Player? player = _players.FirstOrDefault(candidate => candidate.Id == playerId);
        if (player is not null)
            _players.Remove(player);
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
        Player player = new(Guid.NewGuid(), name.Trim(), teamId, skin);
        _players.Add(player);
        AIPlayer aiPlayer = new(player);
        _aiPlayers.Add(player.Id, aiPlayer);
        return aiPlayer;
    }

    public AIPlayer? FindAIPlayer(string idOrName)
    {
        if (Guid.TryParse(idOrName, out Guid id) && _aiPlayers.TryGetValue(id, out AIPlayer? byId))
            return byId;

        return _aiPlayers.Values.FirstOrDefault(ai =>
            string.Equals(ai.Name, idOrName, StringComparison.OrdinalIgnoreCase));
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

        float deltaTime =
            (float)gameTime.ElapsedGameTime.TotalSeconds;

        camera.UpdateMouse(gameTime);
        if (!Globals.Console.IsOpen)
            camera.UpdateKeyboard(gameTime);
        bool actionPanelConsumed =
            ActionPanel?.Update(LocalPlayer.SelectedUnits, viewport) == true;
        if (!actionPanelConsumed)
            LocalPlayer.Update(gameTime, camera, viewport);
        World.Update(gameTime);
        Network.Update();
        _ = NetworkHost.UpdateAsync(gameTime);
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
        //Globals._debugRenderer.DrawGameGrid(World, camera.View, camera.Projection);
        
    }

}
