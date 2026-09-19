using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

public enum ProjectileKind
{
    None,
    BallisticShell,
    Rocket
}

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
    private readonly ProjectileKind _kind;
    private readonly MeshSet? _meshSet;
    private float _elapsed;
    private float _trailElapsed;
    private Vector3 _flightDirection;

    public bool IsExpired => _elapsed >= _duration;
    /// <summary>Projectiles are deliberately influenced far less by wind than smoke.</summary>
    public float WindInfluence { get; set; } = 0.02f;

    public Projectile(
        Vector3 start,
        Vector3 target,
        ProjectileKind kind = ProjectileKind.BallisticShell,
        float speed = 35.0f)
        : base(start)
    {
        _start = start;
        _target = target;
        _kind = kind;
        _duration = Math.Max(0.05f, Vector3.Distance(start, target) / Math.Max(0.1f, speed));
        _flightDirection = target - start;
        if (_flightDirection.LengthSquared() > 0.0001f)
            _flightDirection.Normalize();
        else
            _flightDirection = Vector3.Forward;

        if (kind == ProjectileKind.Rocket)
        {
            if (Globals.MeshHandler.Meshes.TryGetValue("rgp-projectile", out Mesh? rocketMesh) ||
                Globals.MeshHandler.Meshes.TryGetValue("rpg-projectile", out rocketMesh))
            {
                _meshSet = new MeshSet(rocketMesh);
            }
        }
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += deltaSeconds;
        _trailElapsed += deltaSeconds;
        float progress = MathHelper.Clamp(_elapsed / _duration, 0.0f, 1.0f);
        Vector3 position = Vector3.Lerp(_start, _target, progress);
        if (_kind == ProjectileKind.BallisticShell)
            position.Y += MathF.Sin(progress * MathHelper.Pi) * 1.5f;
        position += Globals.World.Weather.GetWind(position) * WindInfluence * _elapsed;

        Vector3 previousPosition = Position;
        Vector3 direction = position - previousPosition;
        if (direction.LengthSquared() > 0.000001f)
            _flightDirection = Vector3.Normalize(direction);
        SetTransform(Matrix.CreateWorld(position, _flightDirection, Vector3.Up));

        float trailInterval = _kind == ProjectileKind.Rocket ? 0.025f : 0.04f;
        while (_trailElapsed >= trailInterval)
        {
            _trailElapsed -= trailInterval;
            Vector3 trailPosition = Position;
            if (_meshSet?.TryGetExhaustWorldPosition(GetWorldMatrix(), out Vector3 exhaust) == true)
                trailPosition = exhaust;
            Globals.World.Particles.EmitRocketTrail(trailPosition, -_flightDirection, _kind == ProjectileKind.Rocket);
        }
    }

    public override Matrix GetWorldMatrix() => Transform;

    public override void Draw(Effect effect)
    {
        if (_meshSet is not null)
        {
            _meshSet.Draw(effect, GetWorldMatrix());
            return;
        }

        effect.Parameters["World"]?.SetValue(Matrix.CreateScale(0.35f) * GetWorldMatrix());
        RenderHelper.DrawMesh(effect, MeshVertices, MeshIndices);
    }
}
