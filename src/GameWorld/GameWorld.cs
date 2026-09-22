using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RTS;

public class GameWorld
{
    private Terrain _terrain;
    public Terrain Terrain => _terrain;
    public UnitHandler Units { get; }
    public MarkerHandler Markers { get; }
    public ProjectileHandler Projectiles { get; }
    public ParticleSystem Particles { get; }
    public DecalHandler Decals { get; }
    public SmokeEmitterHandler SmokeEmitters { get; }
    public WeatherHandler Weather { get; }
    public TiberiumHandler Tiberium { get; }
    public GameGrid GameGrid { get; }
    public PathfindingManager PathfindingManager { get; }
    public VisibilitySystem Visibility { get; }
    public GameplayMarkerHandler GameplayMarkers { get; }
    public Vector3 Center => new Vector3(_terrain.Width * 0.5f, 0.0f, _terrain.Height * 0.5f);
    public bool IsEditorActive => Units.Units.Any(unit => unit is TerrainEditorTool && !unit.IsDying);

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
        GameGrid.BindTerrain(_terrain);
        Units = new UnitHandler();
        Markers = new MarkerHandler();
        Projectiles = new ProjectileHandler();
        Particles = new ParticleSystem();
        Decals = new DecalHandler();
        SmokeEmitters = new SmokeEmitterHandler();
        Weather = new WeatherHandler(terrainWidth, terrainHeight);
        Tiberium = new TiberiumHandler();
        PathfindingManager = new PathfindingManager(this);
        Visibility = new VisibilitySystem(this);
        GameplayMarkers = new GameplayMarkerHandler();
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
        // Imported Blockbench meshes can contain faces with inconsistent
        // winding. A shadow caster must be visible from the light on either
        // side, otherwise a grounded model may disappear behind terrain that
        // was written to the shadow map first.
        GraphicsDevice graphicsDevice = Globals.GraphicsDevice;
        RasterizerState previousRasterizerState = graphicsDevice.RasterizerState;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
        try
        {
            Units.DrawShadow(effect);
            Projectiles.DrawShadow(effect);
        }
        finally
        {
            graphicsDevice.RasterizerState = previousRasterizerState;
        }
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
        unitEffect.Parameters["ShadowTexelSize"]?.SetValue(
            new Vector2(1.0f / shadowTexture.Width, 1.0f / shadowTexture.Height));
        unitEffect.Parameters["LightDirection"]?.SetValue(lightDirection);
        unitEffect.Parameters["Unlit"]?.SetValue(0.0f);
        unitEffect.Parameters["Opacity"]?.SetValue(1.0f);
        // Always bind a valid texture. Meshes without a dedicated mask use
        // MaterialMaskUseTexture=0, but the shader still owns this sampler.
        unitEffect.Parameters["MaterialMaskTexture"]?.SetValue(Globals._whiteTexture);

        Globals.SkinHandler.ApplyToEffect(unitEffect, PlayerSkin.Green);

        Decals.Draw(unitEffect);
        Tiberium.Draw(unitEffect);

        Units.DrawMobileUnits(unitEffect);
        Units.DrawBuildings(unitEffect);

        // Markers, projectiles and particles do not own atlas variants.
        unitEffect.Parameters["UnitTextureUVOffset"]?.SetValue(Vector2.Zero);
        unitEffect.Parameters["MaterialMaskUVOffset"]?.SetValue(Vector2.Zero);
        unitEffect.Parameters["MaterialMaskSourceUVOffset"]?.SetValue(Vector2.Zero);
        unitEffect.Parameters["MaterialMaskUVScale"]?.SetValue(Vector2.Zero);
        unitEffect.Parameters["MaterialMaskUseTexture"]?.SetValue(0.0f);
        unitEffect.Parameters["MaterialMaskDefaultPlayerMask"]?.SetValue(0.0f);
        unitEffect.Parameters["PlayerSkinStrength"]?.SetValue(0.0f);

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
        Weather.Update(gameTime);
        PathfindingManager.Update();
        _terrain.Update(gameTime);
        Units.Update(gameTime);
        Visibility.Update();
        Markers.Update(gameTime);
        Projectiles.Update(gameTime);
        SmokeEmitters.Update(gameTime);
        Decals.Update(gameTime);
        Tiberium.Update(gameTime, allowGrowth: !IsEditorActive);
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
        GameGrid.BindTerrain(_terrain);
        Weather.ResizeWindMap(_terrain.Width, _terrain.Height);
        GameplayMarkers.Load(mapDirectory);
        LoadMapObjects(mapDirectory);
        Tiberium.Load(mapDirectory);
    }

    public void Save(string mapName)
    {
        string mapDirectory = Path.Combine(Globals.MapsDirectory, mapName);
        if (!Directory.Exists(mapDirectory))
            Directory.CreateDirectory(mapDirectory);
        _terrain.Save(mapDirectory);
        GameplayMarkers.Save(mapDirectory);
        SaveMapObjects(mapDirectory);
        Tiberium.Save(mapDirectory);
    }

    public RTS.Network.WorldData GetWorldData() =>
        _terrain.GetWorldData() with
        {
            GameplayMarkers = GameplayMarkers.GetStates(),
            TiberiumCells = Tiberium.GetStates(),
            MapObjects = GetMapObjectStates()
        };

    public void ApplyWorldData(RTS.Network.WorldData worldData)
    {
        _terrain.ApplyWorldData(worldData);
        GameplayMarkers.ApplyStates(worldData.GameplayMarkers);
        ApplyMapObjectStates(worldData.MapObjects);
        Tiberium.ApplyMapStates(worldData.TiberiumCells);
    }

    private MapObjectState[] GetMapObjectStates() => Units.Units.OfType<TiberiumSource>()
        .Select(source => new MapObjectState(source.UnitId, "tiberium-source",
            source.Position.X, source.Position.Y, source.Position.Z)).ToArray();

    private void ApplyMapObjectStates(IEnumerable<MapObjectState>? states)
    {
        Units.RemoveMapObjects<TiberiumSource>();
        if (states is null) return;
        foreach (MapObjectState state in states.Where(state =>
            state.TypeId.Equals("tiberium-source", StringComparison.OrdinalIgnoreCase) &&
            state.Id != Guid.Empty && float.IsFinite(state.X) && float.IsFinite(state.Y) && float.IsFinite(state.Z)))
            Units.SpawnBuilding(state.TypeId, new Vector3(state.X, state.Y, state.Z), state.RotationDegrees, state.Id, Guid.Empty);
    }

    private void SaveMapObjects(string mapDirectory) => File.WriteAllText(
        Path.Combine(mapDirectory, "map-objects.json"),
        JsonSerializer.Serialize(GetMapObjectStates(), new JsonSerializerOptions { WriteIndented = true }));

    private void LoadMapObjects(string mapDirectory)
    {
        string path = Path.Combine(mapDirectory, "map-objects.json");
        ApplyMapObjectStates(File.Exists(path) ? JsonSerializer.Deserialize<MapObjectState[]>(File.ReadAllText(path)) : null);
    }
}
