using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>Local, non-gameplay ground decals such as shell scorch marks.</summary>
public sealed class DecalHandler
{
    private sealed class ScorchDecal(
        Vector3 position,
        TilemapHandler.Tilemap tilemap,
        int tileIndex,
        float size,
        float rotation,
        float lifetime)
    {
        private static readonly int[] Indices = [0, 1, 2, 0, 2, 3];
        private readonly VertexPositionColorNormalTexture[] _vertices = CreateVertices(tilemap, tileIndex);
        private readonly TilemapHandler.Tilemap _tilemap = tilemap;
        private float _lifetime = lifetime;
        private readonly float _maximumSize = size * 1.35f;
        private float _elapsed;

        public Vector3 Position { get; } = position;
        public float Size { get; private set; } = size;
        public float Rotation { get; } = rotation;
        public bool IsExpired => _lifetime > 0.0f && _elapsed >= _lifetime;

        public void Update(GameTime gameTime) => _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;

        /// <summary>Combines repeated impacts into one longer-lived, subtly larger mark.</summary>
        public void Refresh(float lifetime)
        {
            _elapsed = 0.0f;
            _lifetime = Math.Max(_lifetime, lifetime);
            Size = Math.Min(_maximumSize, Size * 1.07f);
        }

        public bool IsNear(Vector3 point, float radius)
        {
            Vector2 difference = new(Position.X - point.X, Position.Z - point.Z);
            return difference.LengthSquared() <= radius * radius;
        }

        public void Draw(Effect effect)
        {
            float opacity = _lifetime <= 0.0f ? 0.72f :
                MathHelper.Clamp((_lifetime - _elapsed) / MathF.Min(_lifetime, 4.0f), 0.0f, 1.0f) * 0.72f;
            foreach (ref VertexPositionColorNormalTexture vertex in _vertices.AsSpan())
                vertex.Color = new Color(1.0f, 1.0f, 1.0f, opacity);

            effect.Parameters["UnitTexture"]?.SetValue(Globals.TextureHandler.GetAtlas(_tilemap.AtlasIndex));
            effect.Parameters["World"]?.SetValue(
                Matrix.CreateScale(Size) * Matrix.CreateRotationY(Rotation) * Matrix.CreateTranslation(Position));
            RenderHelper.DrawMesh(effect, _vertices, Indices);
        }

        private static VertexPositionColorNormalTexture[] CreateVertices(TilemapHandler.Tilemap tilemap, int tileIndex)
        {
            (Vector2 offset, Vector2 scale) = tilemap.GetAtlasUV(tileIndex);
            return
            [
                new(new Vector3(-0.5f, 0.0f, -0.5f), Color.White, Vector3.Up, offset),
                new(new Vector3( 0.5f, 0.0f, -0.5f), Color.White, Vector3.Up, offset + new Vector2(scale.X, 0.0f)),
                new(new Vector3( 0.5f, 0.0f,  0.5f), Color.White, Vector3.Up, offset + scale),
                new(new Vector3(-0.5f, 0.0f,  0.5f), Color.White, Vector3.Up, offset + new Vector2(0.0f, scale.Y))
            ];
        }
    }

    private readonly List<ScorchDecal> _scorchMarks = [];
    public int MaximumScorchMarks { get; set; } = 384;
    public int MaximumRubbleMarks { get; set; } = 1024;
    private readonly List<ScorchDecal> _rubbleMarks = [];

    public void AddScorchMark(Vector3 impactPosition, float size = 1.8f, float lifetimeSeconds = 180.0f)
    {
        if (!Globals.TilemapHandler.TryGet("Scorch", out TilemapHandler.Tilemap tilemap))
            return;

        // Repeated shell impacts should deepen a single mark instead of
        // building an opaque stack of identical decals on one terrain cell.
        float mergeRadius = size * 0.60f;
        ScorchDecal? existing = _scorchMarks.Find(decal => decal.IsNear(impactPosition, mergeRadius));
        if (existing is not null)
        {
            existing.Refresh(lifetimeSeconds);
            return;
        }

        int x = Math.Clamp((int)MathF.Floor(impactPosition.X), 0, Globals.World.Terrain.Width - 1);
        int z = Math.Clamp((int)MathF.Floor(impactPosition.Z), 0, Globals.World.Terrain.Height - 1);
        Vector3 position = new(impactPosition.X, Globals.World.Terrain.GetHeight(x, z) + 0.012f, impactPosition.Z);
        _scorchMarks.Add(new ScorchDecal(position, tilemap, Random.Shared.Next(tilemap.TileCount),
            size * (0.80f + Random.Shared.NextSingle() * 0.40f), Random.Shared.NextSingle() * MathHelper.TwoPi,
            lifetimeSeconds));

        while (_scorchMarks.Count > MaximumScorchMarks)
            _scorchMarks.RemoveAt(0);
    }

    /// <summary>Places one compact, persistent rubble decal on every cell covered by a building.</summary>
    public void AddBuildingRubble(Building building)
    {
        if (!Globals.TilemapHandler.TryGet("Rubble", out TilemapHandler.Tilemap tilemap))
            return;

        Vector3 forward = building.Transform.Forward;
        float yawDegrees = MathHelper.ToDegrees(MathF.Atan2(-forward.X, -forward.Z));
        float cellSize = Globals.World.GameGrid.CellSize;


        var cells = Globals.World.GameGrid.GetFootprintCells(building, building.Position, yawDegrees);

        //  determine center
        float minX = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxZ = float.MinValue;
        foreach (Point cell in cells)
        {
            minX = Math.Min(minX, cell.X);
            minZ = Math.Min(minZ, cell.Y);
            maxX = Math.Max(maxX, cell.X);
            maxZ = Math.Max(maxZ, cell.Y);
        }

        Vector3 center = new((minX + maxX + 1) * 0.5f * cellSize, 0, (minZ + maxZ + 1) * 0.5f * cellSize);
        // determin max distance
        float maxDistance = 0;
        foreach (Point cell in cells)
        {
            Vector3 cellPosition = new((cell.X + 0.5f) * cellSize, 0, (cell.Y + 0.5f) * cellSize);
            maxDistance = Math.Max(maxDistance, (cellPosition - center).Length());
        }

        foreach (Point cell in cells)
        {
            if (!Globals.World.GameGrid.Contains(cell))
                continue;


            var smokeSettings = SmokeEmissionPresets.DestroyBuilding();
            Vector3 pos = new Vector3(cell.X, Globals.World.Terrain.GetHeight(cell.X, cell.Y) + 0.012f, cell.Y);
            Vector3 direction = pos - center;
            Globals.World.Particles.EmitSmoke(pos, direction, smokeSettings);

            float rndX = Random.Shared.NextSingle() *0.5f - 0.25f;
            float rndZ = Random.Shared.NextSingle() *0.5f - 0.25f;
            float x = (cell.X + 0.5f + rndX) * cellSize;
            float z = (cell.Y + 0.5f + rndZ) * cellSize;
            int terrainX = Math.Clamp((int)MathF.Floor(x), 0, Globals.World.Terrain.Width - 1);
            int terrainZ = Math.Clamp((int)MathF.Floor(z), 0, Globals.World.Terrain.Height - 1);

            //  determine offsetY by distance to center (to pile debris)
            float offsetY = (maxDistance - (new Vector3(x, 0, z) - center).Length()) * 0.1f;
            Vector3 position = new(x, Globals.World.Terrain.GetHeight(terrainX, terrainZ) + 0.014f + offsetY, z);
            float size = cellSize * (1.50f + Random.Shared.NextSingle() * 1.5f);
            _rubbleMarks.Add(new ScorchDecal(position, tilemap, Random.Shared.Next(tilemap.TileCount),
                size, Random.Shared.NextSingle() * MathHelper.TwoPi, lifetime: 0.0f));
        }

        while (_rubbleMarks.Count > MaximumRubbleMarks)
            _rubbleMarks.RemoveAt(0);
    }

    public void Update(GameTime gameTime)
    {
        for (int index = _scorchMarks.Count - 1; index >= 0; index--)
        {
            _scorchMarks[index].Update(gameTime);
            if (_scorchMarks[index].IsExpired)
                _scorchMarks.RemoveAt(index);
        }
        for (int index = _rubbleMarks.Count - 1; index >= 0; index--)
        {
            _rubbleMarks[index].Update(gameTime);
            if (_rubbleMarks[index].IsExpired)
                _rubbleMarks.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        if (_scorchMarks.Count == 0 && _rubbleMarks.Count == 0)
            return;

        GraphicsDevice graphicsDevice = Globals.GraphicsDevice;
        BlendState previousBlend = graphicsDevice.BlendState;
        DepthStencilState previousDepth = graphicsDevice.DepthStencilState;
        RasterizerState previousRasterizer = graphicsDevice.RasterizerState;
        graphicsDevice.BlendState = BlendState.NonPremultiplied;
        graphicsDevice.DepthStencilState = DepthStencilState.DepthRead;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.Parameters["Unlit"]?.SetValue(1.0f);
        effect.Parameters["PlayerSkinStrength"]?.SetValue(0.0f);
        try
        {
            foreach (ScorchDecal decal in _scorchMarks)
                decal.Draw(effect);
            foreach (ScorchDecal decal in _rubbleMarks)
                decal.Draw(effect);
        }
        finally
        {
            graphicsDevice.BlendState = previousBlend;
            graphicsDevice.DepthStencilState = previousDepth;
            graphicsDevice.RasterizerState = previousRasterizer;
            effect.Parameters["Unlit"]?.SetValue(0.0f);
        }
    }
}
