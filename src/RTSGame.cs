using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RTS.Network;

namespace RTS;

public class RTSGame
{
    private float _sunAngle = 0.0f;
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
    private GraphicsDevice _graphicsDevice;

    public RTSGame(
        GraphicsDevice graphicsDevice,
        int terrainWidth,
        int terrainHeight,
        Texture2D? heightMapTexture = null)
    {
        Globals.Game = this;
        _graphicsDevice = graphicsDevice;
        Globals.RenderHelper = new RenderHelper(graphicsDevice);

        World = new GameWorld(graphicsDevice, Globals._terrainEffect, terrainWidth, terrainHeight, 1, heightMapTexture);
        Globals.World = World;
        Globals.LocalPlayer = new PlayerHandler(World, World.Markers);
        _shadowMap = new ShadowMap(graphicsDevice);
        Globals.Console = new GameConsole(graphicsDevice);

        ActionPanel = new ActionPanel(graphicsDevice, Globals.ActionIcons);

        Network = new NetworkHandler();
        NetworkInput = new NetworkInput(Network);
        NetworkHost = new NetworkHost(Network, NetworkInput);
        NetworkClient = new NetworkClient(Network);
        _consoleCommands = new ConsoleCommands(Globals.Console, this);

        _ = _consoleCommands.CallBatch(new[] { "autorun.batch" });
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
        if (!Globals.Console.IsOpen)
            camera.UpdateKeyboard(gameTime);
        bool actionPanelConsumed =
            ActionPanel?.Update(LocalPlayer.SelectedUnits, viewport) == true;
        if (!actionPanelConsumed)
            LocalPlayer.Update(gameTime, camera, viewport);
        World.Update(gameTime);
        Network.Update();
        _ = NetworkHost.UpdateAsync();
        UpdateConsole(gameTime);

        ShadowMap.Update(
            World.Center,
            World.Terrain.Width * 1.42f,
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

        LocalPlayer.Draw3D(_graphicsDevice, camera);
        //Globals._debugRenderer.DrawGameGrid(World, camera.View, camera.Projection);
        
    }

}