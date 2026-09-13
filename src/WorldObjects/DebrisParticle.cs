using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

/// <summary>A flying fragment with a smoke trail; it uses Sparks when that tilemap is available.</summary>
public sealed class DebrisParticle : WorldObject
{
    private static readonly int[] Indices = [0, 1, 2, 0, 2, 3];
    private static readonly VertexPositionColorNormalTexture[] FallbackVertices =
    [
        new(new Vector3(-0.5f, 0.0f, -0.5f), Color.OrangeRed, Vector3.Up),
        new(new Vector3(0.5f, 0.0f, -0.5f), Color.Yellow, Vector3.Up),
        new(new Vector3(0.5f, 0.0f, 0.5f), Color.OrangeRed, Vector3.Up),
        new(new Vector3(-0.5f, 0.0f, 0.5f), Color.DarkRed, Vector3.Up)
    ];
    private readonly TilemapHandler.Tilemap? _sparkTilemap;
    private readonly VertexPositionColorNormalTexture[]? _spriteVertices;
    private readonly float _lifetime;
    private readonly float _smokeInterval;
    private readonly SmokeEmissionSettings _smokeSettings;
    private readonly float _rotation;
    private float _elapsed;
    private float _smokeElapsed;

    public Vector3 Velocity { get; private set; }
    public bool IsExpired => _elapsed >= _lifetime;
    public bool UsesSprite => _sparkTilemap is not null;

    public DebrisParticle(Vector3 position, Vector3 velocity, float lifetime, float smokeInterval,
        SmokeEmissionSettings smokeSettings, TilemapHandler.Tilemap? sparkTilemap = null) : base(position)
    {
        Velocity = velocity;
        _lifetime = lifetime;
        _smokeInterval = Math.Max(0.01f, smokeInterval);
        _smokeSettings = smokeSettings;
        _sparkTilemap = sparkTilemap;
        _rotation = Random.Shared.NextSingle() * MathHelper.TwoPi;
        if (sparkTilemap is not null)
        {
            (Vector2 offset, Vector2 scale) = sparkTilemap.GetAtlasUV(Random.Shared.Next(sparkTilemap.TileCount));
            _spriteVertices =
            [
                new(new Vector3(-0.5f, 0.5f, 0.0f), Color.White, Vector3.Backward, offset),
                new(new Vector3(0.5f, 0.5f, 0.0f), Color.White, Vector3.Backward, offset + new Vector2(scale.X, 0.0f)),
                new(new Vector3(0.5f, -0.5f, 0.0f), Color.White, Vector3.Backward, offset + scale),
                new(new Vector3(-0.5f, -0.5f, 0.0f), Color.White, Vector3.Backward, offset + new Vector2(0.0f, scale.Y))
            ];
        }
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += deltaSeconds;
        _smokeElapsed += deltaSeconds;
        Velocity += Vector3.Down * 7.0f * deltaSeconds;
        SetPosition(Position + (Velocity + Globals.World.Weather.GetWind(Position) * 0.15f) * deltaSeconds);
        while (_smokeElapsed >= _smokeInterval && !IsExpired)
        {
            _smokeElapsed -= _smokeInterval;
            Vector3 trailDirection = Velocity.LengthSquared() > 0.001f ? -Vector3.Normalize(Velocity) : Vector3.Up;
            Globals.World.Particles.EmitSmoke(Position, trailDirection, _smokeSettings);
        }
    }

    public override void Draw(Effect effect)
    {
        float progress = MathHelper.Clamp(_elapsed / _lifetime, 0.0f, 1.0f);
        float scale = MathHelper.Lerp(0.14f, 0.025f, progress);
        if (_sparkTilemap is null || _spriteVertices is null)
        {
            effect.Parameters["World"]?.SetValue(Matrix.CreateScale(scale) * Transform);
            RenderHelper.DrawMesh(effect, FallbackVertices, Indices);
            return;
        }

        Color color = new Color(new Vector4(1.0f, 0.75f, 0.25f, 1.0f - progress));
        for (int index = 0; index < _spriteVertices.Length; index++)
            _spriteVertices[index].Color = color;
        Matrix world = Matrix.CreateScale(scale) * Matrix.CreateRotationZ(_rotation + _elapsed * 5.0f) *
            Matrix.CreateBillboard(Position, Globals._camera.Position, Vector3.Up, Vector3.Forward);
        effect.Parameters["UnitTexture"]?.SetValue(Globals.TextureHandler.GetAtlas(_sparkTilemap.AtlasIndex));
        effect.Parameters["UnitTextureUVOffset"]?.SetValue(Vector2.Zero);
        effect.Parameters["World"]?.SetValue(world);
        RenderHelper.DrawMesh(effect, _spriteVertices, Indices);
    }
}
