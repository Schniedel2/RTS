using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;
using System;

namespace RTS;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;

    private SpriteBatch _spriteBatch = null!;

        private RTSGame _rtsGame = null!;
        private int _frameCount;
        private int _framesPerSecond;
        private TimeSpan _fpsElapsedTime;


    public Game1()
    {
        _graphics =
            new GraphicsDeviceManager(this);

        Content.RootDirectory = "Content";

        IsMouseVisible = true;

        _graphics.PreferredBackBufferWidth = 1920;
        _graphics.PreferredBackBufferHeight = 1080;

        _graphics.SynchronizeWithVerticalRetrace = true;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        _rtsGame.Console.HandleTextInput(e.Character);
    }

    protected override void Initialize()
    {
        Globals._debugRenderer = new DebugRenderer(GraphicsDevice);

        Globals._camera = new Camera(_graphics);

        Window.TextInput += OnTextInput;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        Globals._terrainEffect = Content.Load<Effect>("Terrain");
        Globals._shadowEffect = Content.Load<Effect>("ShadowMap");
        Globals._unitEffect = Content.Load<Effect>("Unit");
        Globals._terrainTileSheet = Content.Load<Texture2D>("terrainTileSheet");
        Globals._debugFont = Content.Load<SpriteFont>("DebugFont");
        Texture2D actionIcons = Content.Load<Texture2D>("actionIcons");

        Texture2D? heightMapTexture = LoadHeightMapTexture();

        Globals._terrainEffect.Parameters["TerrainTilesTexture"]?.SetValue(Globals._terrainTileSheet);

        // -------------------------------------------------
        //  white-texture for shadowmap
        // -------------------------------------------------
        Globals._whiteTexture = new Texture2D(
            GraphicsDevice,
            1,
            1);

        Globals._whiteTexture.SetData(
            new[] { Color.White });

        // -------------------------------------------------
        // Terrain
        // -------------------------------------------------

        _rtsGame = new RTSGame(
            graphicsDevice: GraphicsDevice,
              terrainWidth: 512,
              terrainHeight: 512,
              terrainCellSize: 1.0f,
              heightMapTexture: heightMapTexture,
              actionIcons: actionIcons);

        Globals._shadowEffect.Parameters["DebugMode"]?.SetValue(0);
    }

    private Texture2D? LoadHeightMapTexture()
    {
        string heightMapPath = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "Heightmaps",
            "terrain-height.png");

        if (!File.Exists(heightMapPath))
            return null;

        try
        {
            using FileStream stream = File.OpenRead(heightMapPath);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    protected override void Update(GameTime gameTime)
    {        
        _rtsGame.Update(gameTime, Globals._camera, GraphicsDevice.Viewport);
            
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _frameCount++;
        _fpsElapsedTime += gameTime.ElapsedGameTime;

        if (_fpsElapsedTime.TotalSeconds >= 1.0)
        {
            _framesPerSecond = (int)(_frameCount / _fpsElapsedTime.TotalSeconds);
            _frameCount = 0;
            _fpsElapsedTime = TimeSpan.Zero;
        }

        Globals._unitEffect.Parameters["DebugMode"]?.SetValue(0);
        Globals._terrainEffect.Parameters["DebugMode"]?.SetValue(0);

        // =====================================================
        // 1. Shadow Pass
        // =====================================================

        _rtsGame.DrawShadow();

        // =====================================================
        // 2. Normaler Pass
        // =====================================================

        GraphicsDevice.Clear(
            Color.CornflowerBlue);

        _rtsGame.Draw3D(Globals._camera);

      /*
        _debugRenderer.DrawTerrainGrid(
              _rtsGame.World,
            Globals._camera.View,
            Globals._camera.Projection);

        _debugRenderer.DrawTileMapGrid(
              _rtsGame.World,
            Globals._camera.View,
            Globals._camera.Projection);
        */

        // =====================================================
        // Debug Shadow Map
        // =====================================================

        _spriteBatch.Begin();

        _rtsGame.LocalPlayer.Draw(
            _spriteBatch,
            Globals._camera,
            GraphicsDevice.Viewport);

        _spriteBatch.DrawString(
            Globals._debugFont,
            $"FPS: {_framesPerSecond}",
            new Vector2(320, 10),
            Color.Black);

        _rtsGame.Draw2D(_spriteBatch);
        _rtsGame.ActionPanel?.Draw(_spriteBatch, GraphicsDevice.Viewport);

        _spriteBatch.End();

        base.Draw(gameTime);
    }
}