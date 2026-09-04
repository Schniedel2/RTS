using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class GameWorld
{
    private GraphicsDevice _graphicsDevice;
    private Terrain _terrain;
    public Terrain Terrain => _terrain;
    public UnitHandler Units { get; }
    public MarkerHandler Markers { get; }
    public GameGrid GameGrid { get; }
    public PathfindingManager PathfindingManager { get; }
    public Vector3 Center => new Vector3(_terrain.Width * 0.5f, 0.0f, _terrain.Height * 0.5f);

    public GameWorld(
        GraphicsDevice graphicsDevice,
        Effect effect,
        int terrainWidth,
        int terrainHeight,
        int gameGridCellSize,
        Texture2D? heightMapTexture = null)
    {
        _graphicsDevice = graphicsDevice;
        _terrain =
            new Terrain(
                graphicsDevice,
                effect,
                width: terrainWidth,
                height: terrainHeight,
                heightScale: 32.0f,
                heightMapTexture: heightMapTexture);

        GameGrid = new GameGrid(terrainWidth, terrainHeight, gameGridCellSize);
        Units = new UnitHandler(_terrain, this);
        Markers = new MarkerHandler();
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
        Units.DrawShadow(_graphicsDevice, effect);
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

        Units.Draw(_graphicsDevice, unitEffect);
        Markers.Draw(_graphicsDevice, unitEffect);
    }

    public void Update(GameTime gameTime)
    {
        PathfindingManager.Update();
        _terrain.Update(gameTime);
        Units.Update(gameTime);
        Markers.Update(gameTime);
    }

    public bool CanMove(int x, int y, Unit unit)
    {
           return GameGrid.CanPlace(unit, new Point(x, y));
    }
}
