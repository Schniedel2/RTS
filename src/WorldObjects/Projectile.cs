using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public sealed class Projectile : WorldObject
{
    private static readonly VertexPositionColorNormalTexture[] MeshVertices =
    [
        new(new Vector3(-0.5f, 0.0f, -0.5f), Color.OrangeRed, Vector3.Up),
        new(new Vector3(0.5f, 0.0f, -0.5f), Color.OrangeRed, Vector3.Up),
        new(new Vector3(0.5f, 0.0f, 0.5f), Color.OrangeRed, Vector3.Up),
        new(new Vector3(-0.5f, 0.0f, 0.5f), Color.OrangeRed, Vector3.Up)
    ];

    private static readonly int[] MeshIndices = [0, 1, 2, 0, 2, 3];
    private readonly Vector3 _start;
    private readonly Vector3 _target;
    private readonly float _duration;
    private float _elapsed;
    private float _trailElapsed;

    protected override VertexPositionColorNormalTexture[] Vertices => MeshVertices;
    protected override int[] Indices => MeshIndices;
    public bool IsExpired => _elapsed >= _duration;

    public Projectile(Vector3 start, Vector3 target, float duration = 0.35f)
        : base(start)
    {
        _start = start;
        _target = target;
        _duration = duration;
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += deltaSeconds;
        _trailElapsed += deltaSeconds;
        float progress = MathHelper.Clamp(_elapsed / _duration, 0.0f, 1.0f);
        Vector3 position = Vector3.Lerp(_start, _target, progress);
        position.Y += MathF.Sin(progress * MathHelper.Pi) * 1.5f;
        SetPosition(position);

        while (_trailElapsed >= 0.04f)
        {
            _trailElapsed -= 0.04f;
            Globals.World.Particles.EmitRocketTrail(Position);
        }
    }

    protected override Matrix GetWorldMatrix() =>
        Matrix.CreateScale(0.35f) * Transform;
}
