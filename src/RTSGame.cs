using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RTS.Network;

namespace RTS;

public class RTSGame
{
    private float _sunAngle = 0.0f;
    public GameWorld World { get; }
    public PlayerHandler LocalPlayer { get; }
    public RenderHelper RenderHelper { get; }
    private KeyboardState _previousKeyboardState;
    private ConsoleCommands _consoleCommands = null!;
    private GameConsole _console = null!;
    public GameConsole Console => _console;
    private ShadowMap _shadowMap = null!;
    public ShadowMap ShadowMap => _shadowMap;
    public NetworkHandler Network { get; }
    public NetworkInput NetworkInput { get; }
    public NetworkHost NetworkHost { get; }
    public NetworkClient NetworkClient { get; }
    public ActionPanel? ActionPanel { get; }

    public RTSGame(
        GraphicsDevice graphicsDevice,
        int terrainWidth,
        int terrainHeight,
        float terrainCellSize,
        Texture2D? heightMapTexture = null,
        Texture2D? actionIcons = null)
    {
        World = new GameWorld(
            graphicsDevice,
            Globals._terrainEffect,
            terrainWidth,
            terrainHeight,
            terrainCellSize,
            heightMapTexture);
        RenderHelper = new RenderHelper(graphicsDevice);
            LocalPlayer = new PlayerHandler(
                World,
                    World.Markers,
                    RenderHelper);
        _shadowMap = new ShadowMap(graphicsDevice);
        _console = new GameConsole(graphicsDevice);
        ActionPanel = actionIcons is null
            ? null
            : new ActionPanel(graphicsDevice, actionIcons);
        if (ActionPanel is not null)
            ActionPanel.ActionSelected += LocalPlayer.SelectAction;
        Network = new NetworkHandler();
        NetworkInput = new NetworkInput(Network);
        NetworkHost = new NetworkHost(Network, NetworkInput);
        NetworkClient = new NetworkClient(Network);
        _consoleCommands = new ConsoleCommands(_console, this);

        _ = _consoleCommands.CallBatch(new[] { "autorun.batch" });
    }

    private void UpdateConsole(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();

        // check if the circumflex (^) key is pressed   
        if (keyboard.IsKeyDown(Keys.F12) &&
            !_previousKeyboardState.IsKeyDown(Keys.F12))
        {
            _console.Toggle();
        }

        if (_console.IsOpen)
        {
            if (keyboard.IsKeyDown(Keys.Back) &&
                !_previousKeyboardState.IsKeyDown(Keys.Back))
            {
                _console.Backspace();
            }

            if (keyboard.IsKeyDown(Keys.Enter) &&
                !_previousKeyboardState.IsKeyDown(Keys.Enter))
            {
                _console.Execute();
            }

            if (keyboard.IsKeyDown(Keys.Up) &&
                !_previousKeyboardState.IsKeyDown(Keys.Up))
            {
                _console.HistoryUp();
            }

            if (keyboard.IsKeyDown(Keys.Down) &&
                !_previousKeyboardState.IsKeyDown(Keys.Down))
            {
                _console.HistoryDown();
            }
        }

        _previousKeyboardState = keyboard;
    }

    public void Update(GameTime gameTime, Camera camera, Viewport viewport)
    {
        Globals.Telemetry.FramesProcessed++;

        float deltaTime =
            (float)gameTime.ElapsedGameTime.TotalSeconds;
        //_sunAngle += deltaTime * 0.1f;

        camera.UpdateMouse(gameTime);
        if (!_console.IsOpen)
            camera.UpdateKeyboard(gameTime);
        bool actionPanelConsumed =
            ActionPanel?.Update(LocalPlayer.SelectedUnits, viewport) == true;
        if (!actionPanelConsumed)
            LocalPlayer.Update(camera, viewport);
        World.Update(gameTime);
        Network.Update();
        _ = NetworkHost.UpdateAsync();
        UpdateConsole(gameTime);

        ShadowMap.Update(
            World.Center,
            World.Terrain.Width * World.Terrain.CellSize * 1.25f,
            _sunAngle);

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
        if (Globals.Debug_ShadowMap_ShowPreview)
        {
            spriteBatch.Draw(
                ShadowMap.Texture,
                new Rectangle(10, 10, 300, 300),
                Color.White);
        }

        _console.Draw(spriteBatch);
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

        //Globals._debugRenderer.DrawGameGrid(World, camera.View, camera.Projection);
        
    }

}