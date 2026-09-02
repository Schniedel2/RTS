using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

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
    public Effect TerrainEffect = null!;
    public Effect UnitEffect = null!;

    public Effect ShadowEffect = null!;
    public ShadowMap ShadowMap => _shadowMap;

    public RTSGame(
        GraphicsDevice graphicsDevice,
        Effect terrainEffect,
        int terrainWidth,
        int terrainHeight,
        float terrainCellSize)
    {
        World = new GameWorld(
            graphicsDevice,
            terrainEffect,
                terrainWidth,
                terrainHeight,
                terrainCellSize);
          RenderHelper = new RenderHelper(graphicsDevice);
                LocalPlayer = new PlayerHandler(
                    World,
                        World.Markers,
                        RenderHelper);
        _shadowMap = new ShadowMap(graphicsDevice);
        _console = new GameConsole(graphicsDevice);
        _consoleCommands = new ConsoleCommands(_console, this);
    }

    private void UpdateConsole(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();

        // F1 öffnet/schließt die Console
        if (keyboard.IsKeyDown(Keys.F1) &&
            !_previousKeyboardState.IsKeyDown(Keys.F1))
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
        float deltaTime =
            (float)gameTime.ElapsedGameTime.TotalSeconds;
        _sunAngle += deltaTime * 0.1f;

        camera.UpdateMouse(gameTime);
        if (!_console.IsOpen)
            camera.UpdateKeyboard(gameTime);
        LocalPlayer.Update(camera, viewport);
        World.Update(gameTime);
        UpdateConsole(gameTime);

        ShadowMap.Update(
            Vector3.Zero,
            180.0f,
            _sunAngle);

    }

    public void DrawShadow()
    {
        ShadowMap.Begin();

        World.DrawShadow(
            ShadowEffect,
            Matrix.Identity,
            ShadowMap.View,
            ShadowMap.Projection);

        ShadowMap.End();
    }
  
    public void Draw2D(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(
            ShadowMap.Texture,
            new Rectangle(10, 10, 300, 300),
            Color.White);

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
            UnitEffect);

        //_debugRenderer.DrawGameGrid(_rtsGame.World, _camera.View, _camera.Projection);
        
    }

}