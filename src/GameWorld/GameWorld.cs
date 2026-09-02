using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public class GameWorld
{
    private Terrain _terrain;
    public Terrain Terrain => _terrain;
    public UnitHandler Units { get; }
    public MarkerHandler Markers { get; }
    public Pathfinder Pathfinder { get; }
    public GameGrid Grid { get; }
    
    public GameWorld(
        GraphicsDevice graphicsDevice,
        Effect effect,
        int terrainWidth,
        int terrainHeight,
        float terrainCellSize)
    {
        _terrain =
            new Terrain(
                graphicsDevice,
                effect,
                width: terrainWidth,
                height: terrainHeight,
                cellSize: terrainCellSize,
                heightScale: 2.0f);

        Grid = new GameGrid(terrainWidth, terrainHeight, terrainCellSize);
        Units = new UnitHandler(graphicsDevice, _terrain, this);
        Markers = new MarkerHandler(graphicsDevice);
        Pathfinder = new Pathfinder(this);
    }

        public Unit SpawnUnit(
            Vector3 position,
            int length,
            int width,
            float height)
        {
            return Units.SpawnUnit(position, length, width, height);
        }

        public Soldier SpawnSoldier(Vector3 position)
        {
            return Units.SpawnSoldier(position);
        }

        public Car SpawnCar(Vector3 position)
        {
            return Units.SpawnCar(position);
        }

        public Tank SpawnTank(Vector3 position)
        {
            return Units.SpawnTank(position);
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

        Units.Draw(unitEffect);
        Markers.Draw(unitEffect);
    }

    public void Update(GameTime gameTime)
    {
        _terrain.Update(gameTime);
        Units.Update(gameTime);
        Markers.Update(gameTime);
    }

    public bool CanMove(int x, int y, Unit unit)
    {
           return Grid.CanPlace(unit, new Point(x, y));
    }
}
