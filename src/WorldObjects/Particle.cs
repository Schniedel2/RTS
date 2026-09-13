using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace RTS;

public sealed class Particle : WorldObject
{
    private static readonly int[] MeshIndices = [0, 1, 2, 0, 2, 3];
    private readonly VertexPositionColorNormalTexture[] MeshVertices;
    private readonly float _lifetime;
    private readonly float _initialSize;
    private float _remainingLifetime;

    public Vector3 Velocity { get; private set; }
    /// <summary>0 ignores wind; 1 drifts with the complete world wind velocity.</summary>
    public float WindInfluence { get; set; }
    public bool IsExpired => _remainingLifetime <= 0.0f;

    public Particle(
        Vector3 position,
        Vector3 velocity,
        Color color,
        float size,
        float lifetime,
        float windInfluence = 0.0f) : base(position)
    {
        Velocity = velocity;
        _initialSize = size;
        _lifetime = lifetime;
        _remainingLifetime = lifetime;
        WindInfluence = windInfluence;
        MeshVertices =
        [
            new(new Vector3(-0.5f, 0.0f, -0.5f), color, Vector3.Up),
            new(new Vector3(0.5f, 0.0f, -0.5f), color, Vector3.Up),
            new(new Vector3(0.5f, 0.0f, 0.5f), color, Vector3.Up),
            new(new Vector3(-0.5f, 0.0f, 0.5f), color, Vector3.Up)
        ];
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _remainingLifetime -= deltaSeconds;
        Velocity += Vector3.Down * 3.0f * deltaSeconds;
        Vector3 wind = Globals.World.Weather.GetWind(Position);
        SetPosition(Position + (Velocity + wind * WindInfluence) * deltaSeconds);
    }

    protected override Matrix GetWorldMatrix()
    {
        float progress = MathHelper.Clamp(_remainingLifetime / _lifetime, 0.0f, 1.0f);
        float size = _initialSize * (0.35f + progress * 0.65f);
        return Matrix.CreateScale(size) * Transform;
    }

    public override void Draw(Effect effect)
    {        
        effect.Parameters["World"]?.SetValue(GetWorldMatrix());
        RenderHelper.DrawMesh(effect, MeshVertices, MeshIndices);        
    }
}
