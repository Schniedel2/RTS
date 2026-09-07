using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.IO;
using System;

namespace RTS;

public class Game1 : Game
{
    private SpriteBatch _spriteBatch = null!;

        private RTSGame _rtsGame = null!;
        private int _frameCount;
        private int _framesPerSecond;
        private TimeSpan _fpsElapsedTime;


    public Game1()
    {
        Globals.Graphics = new GraphicsDeviceManager(this);

        Content.RootDirectory = "Content";

        IsMouseVisible = true;

        Globals.Graphics.PreferredBackBufferWidth = 1920;
        Globals.Graphics.PreferredBackBufferHeight = 1080;

        Globals.Graphics.SynchronizeWithVerticalRetrace = true;
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        Globals.Console.HandleTextInput(e.Character);
    }

    protected override void Initialize()
    {
        Globals.GraphicsDevice = GraphicsDevice;

        Window.TextInput += OnTextInput;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        Globals._terrainEffect = Content.Load<Effect>("Terrain");
        Globals._shadowEffect = Content.Load<Effect>("ShadowMap");
        Globals._unitEffect = Content.Load<Effect>("Unit");
        Globals._debugFont = Content.Load<SpriteFont>("DebugFont");
        Globals.TooltipFont = Content.Load<SpriteFont>("TooltipFont");
        Globals.ActionIcons = Content.Load<Texture2D>("actionIcons");
        Globals._terrainTileSheet = Content.Load<Texture2D>("terrainTileSheet");


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
              terrainWidth: 512,
              terrainHeight: 512);
        
        Globals._shadowEffect.Parameters["DebugMode"]?.SetValue(0);
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

        _rtsGame.LocalPlayer.Draw2D(
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