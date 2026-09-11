using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Collections.Generic;

namespace RTS;

public class GameWorld
{
    private Terrain _terrain;
    public Terrain Terrain => _terrain;
    public UnitHandler Units { get; }
    public MarkerHandler Markers { get; }
    public ProjectileHandler Projectiles { get; }
    public ParticleSystem Particles { get; }
    public GameGrid GameGrid { get; }
    public PathfindingManager PathfindingManager { get; }
    public Vector3 Center => new Vector3(_terrain.Width * 0.5f, 0.0f, _terrain.Height * 0.5f);

    public GameWorld(
        int terrainWidth,
        int terrainHeight,
        int gameGridCellSize)
    {
        Globals.MapsDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Maps");
        Globals.MapsDirectory = "c:\\temp\\Maps";

        _terrain =
            new Terrain(
                width: terrainWidth,
                height: terrainHeight);

        GameGrid = new GameGrid(terrainWidth, terrainHeight, gameGridCellSize);
        Units = new UnitHandler();
        Markers = new MarkerHandler();
        Projectiles = new ProjectileHandler();
        Particles = new ParticleSystem();
        PathfindingManager = new PathfindingManager(this);
    }

    public void DrawShadow(
        Effect effect,
        Matrix world,
        Matrix view,
        Matrix projection)
    {
        effect.Parameters["View"]?.SetValue(view);
        effect.Parameters["Projection"]?.SetValue(projection);        
        _terrain.DrawShadow(
            effect,
            world,
            view,
            projection);

        effect.Parameters["View"]?.SetValue(view);
        effect.Parameters["Projection"]?.SetValue(projection);
        Units.DrawShadow(effect);
    }

    public void DrawTerrain(
        Matrix world,
        Matrix view,
        Matrix projection,
        ShadowMap shadowMap)
    {
        Matrix lightView = shadowMap.View;
        Matrix lightProjection = shadowMap.Projection;
        Texture2D shadowTexture = shadowMap.Texture;
        Vector3 lightDirection = shadowMap.LightDirection;

        _terrain.Draw(
            world,
            view,
            projection,
            lightView,
            lightProjection,
            shadowTexture,
            lightDirection);
    }

    public void DrawUnits(
        Matrix view,
        Matrix projection,
        ShadowMap shadowMap,
        Effect unitEffect)
    {
        Matrix lightView = shadowMap.View;
        Matrix lightProjection = shadowMap.Projection;
        Texture2D shadowTexture = shadowMap.Texture;
        Vector3 lightDirection = shadowMap.LightDirection;

        unitEffect.Parameters["View"]?.SetValue(view);
        unitEffect.Parameters["Projection"]?.SetValue(projection);
        unitEffect.Parameters["LightView"]?.SetValue(lightView);
        unitEffect.Parameters["LightProjection"]?.SetValue(lightProjection);
        unitEffect.Parameters["ShadowTexture"]?.SetValue(shadowTexture);
        unitEffect.Parameters["LightDirection"]?.SetValue(lightDirection);

        Vector3 playerColor = Globals.Game.Players.Count > 0
            ? Globals.Game.Players[0].Color.ToVector3()
            : Color.Red.ToVector3();
        unitEffect.Parameters["PlayerColor"].SetValue(playerColor);
        unitEffect.Parameters["PlayerColorStrength"].SetValue(1.0f);

        unitEffect.Parameters["UnitTexture"].SetValue(Globals.UnitsTexture);
        unitEffect.Parameters["MaterialMaskTexture"].SetValue(Globals.UnitsMaterialMask);
        Units.DrawMobileUnits(unitEffect);

        unitEffect.Parameters["UnitTexture"].SetValue(Globals.BuildingsTexture);
        Units.DrawBuildings(unitEffect);

        // Markers, projectiles and particles do not own atlas variants.
        unitEffect.Parameters["UnitTextureUVOffset"]?.SetValue(Vector2.Zero);
        unitEffect.Parameters["MaterialMaskUVOffset"]?.SetValue(Vector2.Zero);

        Markers.Draw(unitEffect);
        Projectiles.Draw(unitEffect);
        Particles.Draw(unitEffect);
    }

    public void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        Units.Draw2D(spriteBatch, camera, viewport);
    }

    public void Update(GameTime gameTime)
    {
        PathfindingManager.Update();
        _terrain.Update(gameTime);
        Units.Update(gameTime);
        Markers.Update(gameTime);
        Projectiles.Update(gameTime);
        Particles.Update(gameTime);
    }

    public bool CanMove(int x, int y, MobileUnit unit)
    {
           return GameGrid.CanPlace(unit, new Point(x, y));
    }

    public void Load(string mapName)
    {
        string mapDirectory = Path.Combine(Globals.MapsDirectory, mapName);
        _terrain = new Terrain(mapDirectory);
    }

    public void Save(string mapName)
    {
        string mapDirectory = Path.Combine(Globals.MapsDirectory, mapName);
        if (!Directory.Exists(mapDirectory))
            Directory.CreateDirectory(mapDirectory);
        _terrain.Save(mapDirectory);
    }
}
