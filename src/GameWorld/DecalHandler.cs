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

    public void Update(GameTime gameTime)
    {
        for (int index = _scorchMarks.Count - 1; index >= 0; index--)
        {
            _scorchMarks[index].Update(gameTime);
            if (_scorchMarks[index].IsExpired)
                _scorchMarks.RemoveAt(index);
        }
    }

    public void Draw(Effect effect)
    {
        if (_scorchMarks.Count == 0)
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
