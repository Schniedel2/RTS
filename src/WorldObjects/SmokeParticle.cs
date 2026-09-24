using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

/// <summary>A camera-facing, textured smoke puff sampled from a TilemapHandler tile.</summary>
public sealed class SmokeParticle : WorldObject
{
    private static readonly int[] Indices = [0, 1, 2, 0, 2, 3];
    private readonly VertexPositionColorNormalTexture[] _vertices;
    private readonly int _atlasIndex;
    private readonly float _lifetime;
    private readonly float _startSize;
    private readonly float _endSize;
    private readonly float _rotationRadians;
    private readonly float _rotationSpeedRadians;
    private readonly Color _color;
    private readonly float _opacity;
    private readonly float _buoyancy;
    private readonly float _startDelay;
    private float _elapsed;

    public Vector3 Velocity { get; private set; }
    public float WindInfluence { get; }
    public bool HasStarted => _elapsed >= _startDelay;
    public bool IsExpired => _elapsed >= _startDelay + _lifetime;

    public SmokeParticle(Vector3 position, Vector3 velocity, TilemapHandler.Tilemap tilemap,
        int tileIndex, float startSize, float endSize, float lifetime,
        float rotationRadians, float rotationSpeedRadians, Color color, float opacity,
        float windInfluence, float buoyancy = 0.35f, float startDelay = 0.0f) : base(position)
    {
        (Vector2 uvOffset, Vector2 uvScale) = tilemap.GetAtlasUV(tileIndex);
        _atlasIndex = tilemap.AtlasIndex;
        Velocity = velocity;
        _startSize = startSize;
        _endSize = endSize;
        _lifetime = lifetime;
        _rotationRadians = rotationRadians;
        _rotationSpeedRadians = rotationSpeedRadians;
        _color = color;
        _opacity = opacity;
        _buoyancy = buoyancy;
        _startDelay = MathF.Max(0.0f, startDelay);
        WindInfluence = windInfluence;
        _vertices =
        [
            new(new Vector3(-0.5f, 0.5f, 0.0f), Color.White, Vector3.Backward, uvOffset),
            new(new Vector3(0.5f, 0.5f, 0.0f), Color.White, Vector3.Backward, uvOffset + new Vector2(uvScale.X, 0.0f)),
            new(new Vector3(0.5f, -0.5f, 0.0f), Color.White, Vector3.Backward, uvOffset + uvScale),
            new(new Vector3(-0.5f, -0.5f, 0.0f), Color.White, Vector3.Backward, uvOffset + new Vector2(0.0f, uvScale.Y))
        ];
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        float previousElapsed = _elapsed;
        _elapsed += deltaSeconds;
        float activeDeltaSeconds = MathF.Max(0.0f, _elapsed - _startDelay) -
            MathF.Max(0.0f, previousElapsed - _startDelay);
        if (activeDeltaSeconds <= 0.0f)
            return;

        Velocity += Vector3.Up * _buoyancy * activeDeltaSeconds;
        Vector3 wind = Globals.World.Weather.GetWind(Position);
        SetPosition(Position + (Velocity + wind * WindInfluence) * activeDeltaSeconds);
    }

    public override void Draw(Effect effect)
    {
        if (!HasStarted)
            return;

        float activeElapsed = _elapsed - _startDelay;
        float progress = MathHelper.Clamp(activeElapsed / _lifetime, 0.0f, 1.0f);
        Color color = new Color(new Vector4(
            _color.ToVector3(),
            MathHelper.Clamp(MathF.Pow(1.0f - progress, 1.35f) * _opacity, 0.0f, 1.0f)));
        for (int index = 0; index < _vertices.Length; index++)
            _vertices[index].Color = color;

        float size = MathHelper.Lerp(_startSize, _endSize, progress);
        Matrix world = Matrix.CreateScale(size) *
            Matrix.CreateRotationZ(_rotationRadians + _rotationSpeedRadians * activeElapsed) *
            Matrix.CreateBillboard(Position, Globals._camera.Position, Vector3.Up, Vector3.Forward);
        effect.Parameters["UnitTexture"]?.SetValue(Globals.TextureHandler.GetAtlas(_atlasIndex));
        effect.Parameters["UnitTextureUVOffset"]?.SetValue(Vector2.Zero);
        effect.Parameters["World"]?.SetValue(world);
        RenderHelper.DrawMesh(effect, _vertices, Indices);
    }
}
