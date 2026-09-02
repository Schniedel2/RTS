using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;

    private SpriteBatch _spriteBatch = null!;

    private Effect _shadowEffect = null!;
    private Effect _terrainEffect = null!;
    private Effect _unitEffect = null!;

    private Camera _camera = null!;
    private Texture2D _whiteTexture = null!;
    private Texture2D _terrainTileSheet = null!;
        private RTSGame _rtsGame = null!;
    private DebugRenderer _debugRenderer = null!;
        private SpriteFont _debugFont = null!;
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
        _debugRenderer = new DebugRenderer(GraphicsDevice);

        _camera = new Camera(_graphics);

        Window.TextInput += OnTextInput;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _terrainEffect = Content.Load<Effect>("Terrain");
        _shadowEffect = Content.Load<Effect>("ShadowMap");
        _unitEffect = Content.Load<Effect>("Unit");
        _terrainTileSheet = Content.Load<Texture2D>("terrainTileSheet");
        _debugFont = Content.Load<SpriteFont>("DebugFont");

        _terrainEffect.Parameters["TerrainTilesTexture"]?.SetValue(_terrainTileSheet);

        // -------------------------------------------------
        //  white-texture for shadowmap
        // -------------------------------------------------
        _whiteTexture = new Texture2D(
            GraphicsDevice,
            1,
            1);

        _whiteTexture.SetData(
            new[] { Color.White });

        // -------------------------------------------------
        // Terrain
        // -------------------------------------------------

        _rtsGame = new RTSGame(
            graphicsDevice: GraphicsDevice,
            terrainEffect: _terrainEffect,
              terrainWidth: 512,
              terrainHeight: 512,
              terrainCellSize: 1.0f);

        _rtsGame.Console.Font = _debugFont;
        _rtsGame.ShadowEffect = _shadowEffect;
        _rtsGame.TerrainEffect = _terrainEffect;
        _rtsGame.UnitEffect = _unitEffect;

        _shadowEffect.Parameters["DebugMode"]?.SetValue(0);

        _rtsGame.World.SpawnSoldier(new Vector3(66.0f, 64.0f, 66.0f));
        _rtsGame.World.SpawnCar(new Vector3(82.0f, 56.0f, 66.0f));
        _rtsGame.World.SpawnTank(new Vector3(66.0f, 48.0f, 82.0f));
    }

    protected override void Update(GameTime gameTime)
    {        
        _rtsGame.Update(gameTime, _camera, GraphicsDevice.Viewport);
            
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

        _unitEffect.Parameters["DebugMode"]?.SetValue(0);
        _terrainEffect.Parameters["DebugMode"]?.SetValue(0);

        // =====================================================
        // 1. Shadow Pass
        // =====================================================

        _rtsGame.DrawShadow();

        // =====================================================
        // 2. Normaler Pass
        // =====================================================

        GraphicsDevice.Clear(
            Color.CornflowerBlue);

        _rtsGame.Draw3D(_camera);

      /*
        _debugRenderer.DrawTerrainGrid(
              _rtsGame.Map,
            _camera.View,
            _camera.Projection);

        _debugRenderer.DrawTileMapGrid(
              _rtsGame.Map,
            _camera.View,
            _camera.Projection);
        */

        // =====================================================
        // Debug Shadow Map
        // =====================================================

        _spriteBatch.Begin();

        _rtsGame.LocalPlayer.Draw(
            _spriteBatch,
            _camera,
            GraphicsDevice.Viewport);

        _spriteBatch.DrawString(
            _debugFont,
            $"FPS: {_framesPerSecond}",
            new Vector2(320, 10),
            Color.Black);

        _rtsGame.Draw2D(_spriteBatch);

        _spriteBatch.End();

        base.Draw(gameTime);
    }
}