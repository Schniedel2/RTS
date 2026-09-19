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
    public const float ReplicatedRenderDelay = 0.075f;
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
    private readonly Vector3 _initialVelocity;
    private readonly ProjectileFlightProfile? _flightProfile;
    private readonly bool _waitForAuthoritativeImpact;
    private float _elapsed;
    private Vector3? _lastExhaustPosition;
    private float _trailDistanceSinceEmission;
    private bool _wasEmittingTrail;
    private Vector3 _flightDirection;

    public Guid ProjectileId { get; }
    public bool IsExpired => _elapsed >= _duration;
    public bool ExplodeOnExpiry => !_waitForAuthoritativeImpact;
    /// <summary>Projectiles are deliberately influenced far less by wind than smoke.</summary>
    public float WindInfluence { get; set; } = 0.02f;

    public Projectile(
        Vector3 start,
        Vector3 target,
        ProjectileKind kind = ProjectileKind.BallisticShell,
        float speed = 35.0f)
        : base(start)
    {
        ProjectileId = Guid.NewGuid();
        _start = start;
        _target = target;
        _kind = kind;
        _duration = Math.Max(0.05f, Vector3.Distance(start, target) / Math.Max(0.1f, speed));
        _flightDirection = target - start;
        if (_flightDirection.LengthSquared() > 0.0001f)
            _flightDirection.Normalize();
        else
            _flightDirection = Vector3.Forward;

        _meshSet = CreateProjectileMesh(kind);
    }

    public Projectile(
        Guid projectileId,
        Vector3 start,
        Vector3 initialVelocity,
        ProjectileKind kind)
        : base(start)
    {
        ProjectileId = projectileId;
        _start = start;
        _target = start;
        _initialVelocity = initialVelocity;
        _kind = kind;
        _flightProfile = ProjectileFlightProfile.For(kind);
        _duration = _flightProfile.MaximumLifetime + 2.0f;
        _waitForAuthoritativeImpact = true;
        _flightDirection = initialVelocity.LengthSquared() > 0.0001f
            ? Vector3.Normalize(initialVelocity)
            : Vector3.Forward;
        _meshSet = CreateProjectileMesh(kind);
    }

    public override void Update(GameTime gameTime)
    {
        float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsed += deltaSeconds;
        Vector3 position;
        Vector3 velocity;
        if (_flightProfile is not null)
        {
            float visualAge = Math.Max(0.0f, _elapsed - ReplicatedRenderDelay);
            ProjectileTrajectory.Evaluate(
                _start,
                _initialVelocity,
                visualAge,
                _flightProfile,
                out position,
                out velocity);
        }
        else
        {
            float progress = MathHelper.Clamp(_elapsed / _duration, 0.0f, 1.0f);
            position = Vector3.Lerp(_start, _target, progress);
            if (_kind == ProjectileKind.BallisticShell)
                position.Y += MathF.Sin(progress * MathHelper.Pi) * 1.5f;
            position += Globals.World.Weather.GetWind(position) * WindInfluence * _elapsed;
            velocity = position - Position;
        }

        if (velocity.LengthSquared() > 0.000001f)
            _flightDirection = Vector3.Normalize(velocity);
        SetTransform(Matrix.CreateWorld(position, _flightDirection, Vector3.Up));

        float visualAgeForEffects = Math.Max(0.0f, _elapsed - ReplicatedRenderDelay);
        float exhaustStrength = _flightProfile is null
            ? (_kind == ProjectileKind.Rocket ? 1.0f : 0.0f)
            : ProjectileTrajectory.GetExhaustStrength(visualAgeForEffects, _flightProfile);
        bool motorActive = _flightProfile is null
            ? _kind == ProjectileKind.Rocket
            : ProjectileTrajectory.IsMotorBurning(visualAgeForEffects, _flightProfile);
        Vector3 exhaustPosition = Position;
        if (_meshSet?.TryGetExhaustWorldPosition(GetWorldMatrix(), out Vector3 exhaust) == true)
            exhaustPosition = exhaust;
        UpdateRocketTrail(
            exhaustPosition,
            exhaustStrength,
            motorActive);
    }

    /// <summary>
    /// Places emission points at a stable world-space distance along the
    /// complete segment travelled by the exhaust since the previous update.
    /// This avoids dotted trails when a fast projectile crosses a large
    /// distance during one rendered frame.
    /// </summary>
    private void UpdateRocketTrail(
        Vector3 currentExhaustPosition,
        float exhaustStrength,
        bool motorActive)
    {
        bool emitTrail = _kind == ProjectileKind.Rocket && exhaustStrength > 0.015f;
        if (!emitTrail)
        {
            _lastExhaustPosition = currentExhaustPosition;
            _trailDistanceSinceEmission = 0.0f;
            _wasEmittingTrail = false;
            return;
        }

        // Emit once immediately as the motor/exhaust becomes visible. Do not
        // connect the trail across the preceding motor-delay phase.
        if (!_wasEmittingTrail || _lastExhaustPosition is not Vector3 previousPosition)
        {
            EmitRocketTrailAt(currentExhaustPosition, exhaustStrength, motorActive);
            _lastExhaustPosition = currentExhaustPosition;
            _trailDistanceSinceEmission = 0.0f;
            _wasEmittingTrail = true;
            return;
        }

        Vector3 segment = currentExhaustPosition - previousPosition;
        float remainingDistance = segment.Length();
        float spacing = Math.Max(0.02f, Globals.World.Particles.RocketTrailSpacing);
        if (remainingDistance > 0.0001f)
        {
            Vector3 segmentDirection = segment / remainingDistance;
            Vector3 emissionPosition = previousPosition;

            while (_trailDistanceSinceEmission + remainingDistance >= spacing)
            {
                float distanceToEmission = spacing - _trailDistanceSinceEmission;
                emissionPosition += segmentDirection * distanceToEmission;
                remainingDistance -= distanceToEmission;
                EmitRocketTrailAt(emissionPosition, exhaustStrength, motorActive);
                _trailDistanceSinceEmission = 0.0f;
            }

            _trailDistanceSinceEmission += remainingDistance;
        }

        _lastExhaustPosition = currentExhaustPosition;
        _wasEmittingTrail = true;
    }

    private void EmitRocketTrailAt(
        Vector3 position,
        float exhaustStrength,
        bool motorActive)
    {
        Globals.World.Particles.EmitRocketTrail(
            position,
            -_flightDirection,
            exhaustStrength,
            motorActive);
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

    /// <summary>
    /// Projectiles use the same geometry and world transform in the shadow
    /// pass as in the regular scene pass. Particle trails remain visual-only.
    /// </summary>
    public override void DrawShadow(Effect effect)
    {
        Draw(effect);
    }

    private static MeshSet? CreateProjectileMesh(ProjectileKind kind)
    {
        if (kind != ProjectileKind.Rocket)
            return null;
        if (Globals.MeshHandler.Meshes.TryGetValue("rpg-projectile", out Mesh? rocketMesh))            
            return new MeshSet(rocketMesh);
        return null;
    }
}
