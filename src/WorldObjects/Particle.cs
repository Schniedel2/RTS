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
    public bool IsExpired => _remainingLifetime <= 0.0f;

    public Particle(
        Vector3 position,
        Vector3 velocity,
        Color color,
        float size,
        float lifetime) : base(position)
    {
        Velocity = velocity;
        _initialSize = size;
        _lifetime = lifetime;
        _remainingLifetime = lifetime;
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
        SetPosition(Position + Velocity * deltaSeconds);
    }

    protected override Matrix GetWorldMatrix()
    {
        float progress = MathHelper.Clamp(_remainingLifetime / _lifetime, 0.0f, 1.0f);
        float size = _initialSize * (0.35f + progress * 0.65f);
        return Matrix.CreateScale(size) * Transform;
    }

    public override void Draw(Effect effect)
    {        
        RenderHelper.DrawMesh(effect, MeshVertices, MeshIndices);        
    }
}
