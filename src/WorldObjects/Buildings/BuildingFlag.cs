using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace RTS;

/// <summary>
/// Small client-only cloth flag. Its first vertex column is pinned to a
/// Blockbench <c>pivot:flag</c>; all other vertices are spring constrained.
/// </summary>
public sealed class BuildingFlag
{
    private const int Columns = 5;
    private const int Rows = 4;
    private const float Width = 1.25f;
    private const float Height = 0.72f;
    private const float Gravity = 3.0f;
    private const float WindInfluence = 14.4f;
    private const float Damping = 0.94f;
    private const int ConstraintIterations = 1;
    // At full wind, the outer edge may travel about +/- 0.28 world units.
    // This is intentionally a strong, readable RTS-scale cloth movement.
    private const float MaximumFlutterAmplitude = 0.28f;
    private const float FlutterSpring = 90.0f;
    private const float FlutterDamping = 8.0f;
    private const float WindOrientationThreshold = 0.05f;

    private readonly Vector3[] _positions = new Vector3[Columns * Rows];
    private readonly Vector3[] _velocities = new Vector3[Columns * Rows];
    private readonly VertexPositionColorNormalTexture[] _vertices = new VertexPositionColorNormalTexture[Columns * Rows];
    private readonly int[] _indices = CreateIndices();
    private bool _initialized;
    private float _flutterTime;
    private Vector3 _anchor;
    private Vector3 _right = Vector3.Right;
    private Vector3 _modelRight = Vector3.Right;
    private Vector3 _up = Vector3.Up;

    public bool IsAvailable { get; private set; }
    /// <summary>Radius of the mast clearance cylinder around <c>pivot:flag</c>.</summary>
    public float MastClearance { get; set; } = 0.20f;
    /// <summary>Maximum rotation rate around the mast when the wind changes direction.</summary>
    public float WindTurnDegreesPerSecond { get; set; } = 240.0f;

    public void Update(Building building, GameTime gameTime)
    {
        if (!building.TryGetFlagPivotWorldTransform(out Matrix pivotWorld))
        {
            IsAvailable = false;
            _initialized = false;
            return;
        }

        float deltaSeconds = MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0.0f, 1.0f / 20.0f);
        IsAvailable = true;
        UpdateFrame(pivotWorld, deltaSeconds);
        if (!_initialized)
            Initialize();
        if (deltaSeconds <= 0.0f)
            return;

        _flutterTime += deltaSeconds;

        PinMastVertices();
        for (int row = 0; row < Rows; row++)
        for (int column = 1; column < Columns; column++)
        {
            int index = GetIndex(column, row);
            Vector3 wind = Globals.World.Weather.GetWind(_positions[index]);
            // Wind is intentionally aerodynamic rather than a direct offset:
            // calm weather lets gravity pull the free edge down.
            Vector3 acceleration = Vector3.Down * Gravity +
                (wind - _velocities[index]) * WindInfluence;

            float edgeFactor = column / (float)(Columns - 1);
            float windStrength = wind.Length();

            // A constant wind alone creates a stable sail shape. Drive a
            // travelling target wave along the cloth normal instead. This
            // gives the outer edge a deliberately visible +/- 0.28 movement
            // at full wind, while the mast edge remains perfectly fixed.
            if (windStrength > 0.02f)
            {
                Vector3 clothNormal = Vector3.Cross(_up, _right);
                clothNormal = clothNormal.LengthSquared() > 0.0001f
                    ? Vector3.Normalize(clothNormal)
                    : Vector3.Forward;
                float phase = _flutterTime * (7.0f + windStrength * 2.0f) -
                    edgeFactor * 8.0f + row * 0.75f;
                float flutter = (MathF.Sin(phase) + MathF.Sin(phase * 1.73f + 0.6f) * 0.35f) / 1.35f;
                float windFactor = MathHelper.Clamp(windStrength / 1.2f, 0.0f, 1.0f);
                float targetOffset = flutter * MaximumFlutterAmplitude *
                    edgeFactor * edgeFactor * windFactor;
                float currentOffset = Vector3.Dot(
                    _positions[index] - GetRestPosition(column, row), clothNormal);
                float normalVelocity = Vector3.Dot(_velocities[index], clothNormal);
                acceleration += clothNormal *
                    ((targetOffset - currentOffset) * FlutterSpring - normalVelocity * FlutterDamping);
            }
            _velocities[index] = (_velocities[index] + acceleration * deltaSeconds) * Damping;
            _positions[index] += _velocities[index] * deltaSeconds;
        }

        for (int iteration = 0; iteration < ConstraintIterations; iteration++)
        {
            PinMastVertices();
            ConstrainHorizontal();
            ConstrainVertical();
            ConstrainMastClearance();
        }
        PinMastVertices();
        ConstrainMastClearance();
    }

    public void Draw(Effect effect, Color color)
    {
        if (!IsAvailable || !_initialized)
            return;

        UpdateVertices(color);
        effect.Parameters["World"]?.SetValue(Matrix.Identity);
        effect.Parameters["UnitTexture"]?.SetValue(Globals._whiteTexture);
        effect.Parameters["UnitTextureUVOffset"]?.SetValue(Vector2.Zero);
        effect.Parameters["MaterialMaskUseTexture"]?.SetValue(0.0f);
        effect.Parameters["MaterialMaskDefaultPlayerMask"]?.SetValue(0.0f);
        effect.Parameters["PlayerSkinStrength"]?.SetValue(0.0f);
        effect.Parameters["Opacity"]?.SetValue(1.0f);
        RenderHelper.DrawMesh(effect, _vertices, _indices);
    }

    public void DrawShadow(Effect effect)
    {
        if (!IsAvailable || !_initialized)
            return;

        UpdateVertices(Color.White);
        effect.Parameters["World"]?.SetValue(Matrix.Identity);
        RenderHelper.DrawMesh(effect, _vertices, _indices);
    }

    private void UpdateFrame(Matrix pivotWorld, float deltaSeconds)
    {
        _anchor = pivotWorld.Translation;
        _modelRight = Vector3.TransformNormal(Vector3.Right, pivotWorld);
        _up = Vector3.TransformNormal(Vector3.Up, pivotWorld);
        if (_modelRight.LengthSquared() < 0.0001f)
            _modelRight = Vector3.Right;
        else
            _modelRight.Normalize();
        if (_up.LengthSquared() < 0.0001f)
            _up = Vector3.Up;
        else
            _up.Normalize();

        if (!_initialized)
            _right = _modelRight;

        // The cloth leaves the mast downwind. The model's local X axis is its
        // rest direction when the air is calm. Rotate rather than snap, so a
        // 180 degree wind change visibly wraps the flag around the mast.
        Vector3 wind = Globals.World.Weather.GetWind(_anchor);
        Vector3 horizontalWind = wind - _up * Vector3.Dot(wind, _up);
        Vector3 targetDirection = horizontalWind.LengthSquared() >=
            WindOrientationThreshold * WindOrientationThreshold
            ? Vector3.Normalize(horizontalWind)
            : _modelRight;
        _right = RotateTowardsAroundMast(_right, targetDirection, deltaSeconds);
    }

    private Vector3 RotateTowardsAroundMast(Vector3 current, Vector3 target, float deltaSeconds)
    {
        current -= _up * Vector3.Dot(current, _up);
        target -= _up * Vector3.Dot(target, _up);
        if (current.LengthSquared() < 0.0001f)
            current = _modelRight;
        else
            current.Normalize();
        if (target.LengthSquared() < 0.0001f)
            return current;
        target.Normalize();

        float dot = MathHelper.Clamp(Vector3.Dot(current, target), -1.0f, 1.0f);
        float angle = MathF.Acos(dot);
        float maximumStep = MathHelper.ToRadians(Math.Max(0.0f, WindTurnDegreesPerSecond)) * deltaSeconds;
        if (angle <= maximumStep || maximumStep <= 0.0f)
            return angle <= maximumStep ? target : current;

        float sign = Vector3.Dot(Vector3.Cross(current, target), _up) >= 0.0f ? 1.0f : -1.0f;
        return Vector3.Normalize(Vector3.TransformNormal(
            current, Matrix.CreateFromAxisAngle(_up, sign * maximumStep)));
    }

    private void Initialize()
    {
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
        {
            int index = GetIndex(column, row);
            _positions[index] = GetRestPosition(column, row) +
                Vector3.Down * (column / (float)(Columns - 1) * 0.22f);
            _velocities[index] = Vector3.Zero;
        }
        PinMastVertices();
        _initialized = true;
    }

    private Vector3 GetRestPosition(int column, int row) =>
        _anchor + _right * (MastClearance + Width * column / (Columns - 1)) -
        _up * (Height * row / (Rows - 1));

    private void PinMastVertices()
    {
        for (int row = 0; row < Rows; row++)
        {
            int index = GetIndex(0, row);
            _positions[index] = GetRestPosition(0, row);
            _velocities[index] = Vector3.Zero;
        }
    }

    private void ConstrainHorizontal()
    {
        float restDistance = Width / (Columns - 1);
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns - 1; column++)
            ConstrainPair(GetIndex(column, row), GetIndex(column + 1, row), restDistance, column == 0);
    }

    private void ConstrainVertical()
    {
        float restDistance = Height / (Rows - 1);
        for (int row = 0; row < Rows - 1; row++)
        for (int column = 0; column < Columns; column++)
            ConstrainPair(GetIndex(column, row), GetIndex(column, row + 1), restDistance, column == 0);
    }

    /// <summary>Keeps free cloth vertices outside a mast-sized cylinder.</summary>
    private void ConstrainMastClearance()
    {
        float clearance = Math.Max(0.0f, MastClearance);
        if (clearance <= 0.0f)
            return;

        for (int row = 0; row < Rows; row++)
        for (int column = 1; column < Columns; column++)
        {
            int index = GetIndex(column, row);
            Vector3 offset = _positions[index] - _anchor;
            // The mast is aligned to the pivot's local up axis, so only the
            // radial distance around that axis is relevant.
            Vector3 radial = offset - _up * Vector3.Dot(offset, _up);
            float distance = radial.Length();
            if (distance >= clearance)
                continue;
            Vector3 direction = distance > 0.0001f ? radial / distance : _right;
            _positions[index] += direction * (clearance - distance);
        }
    }

    private void ConstrainPair(int first, int second, float restDistance, bool firstIsPinned)
    {
        Vector3 offset = _positions[second] - _positions[first];
        float distance = offset.Length();
        if (distance <= 0.00001f)
            return;
        Vector3 correction = offset * ((distance - restDistance) / distance);
        if (firstIsPinned)
        {
            _positions[second] -= correction;
            return;
        }
        _positions[first] += correction * 0.5f;
        _positions[second] -= correction * 0.5f;
    }

    private void UpdateVertices(Color color)
    {
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
        {
            int index = GetIndex(column, row);
            Vector3 horizontal = column == Columns - 1
                ? _positions[index] - _positions[GetIndex(column - 1, row)]
                : _positions[GetIndex(column + 1, row)] - _positions[index];
            Vector3 vertical = row == Rows - 1
                ? _positions[index] - _positions[GetIndex(column, row - 1)]
                : _positions[GetIndex(column, row + 1)] - _positions[index];
            Vector3 normal = Vector3.Cross(vertical, horizontal);
            normal = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.Forward;
            _vertices[index] = new VertexPositionColorNormalTexture(
                _positions[index], color, normal,
                new Vector2(column / (float)(Columns - 1), row / (float)(Rows - 1)));
        }
    }

    private static int GetIndex(int column, int row) => row * Columns + column;

    private static int[] CreateIndices()
    {
        int[] indices = new int[(Columns - 1) * (Rows - 1) * 12];
        int next = 0;
        for (int row = 0; row < Rows - 1; row++)
        for (int column = 0; column < Columns - 1; column++)
        {
            int topLeft = GetIndex(column, row);
            int topRight = GetIndex(column + 1, row);
            int bottomLeft = GetIndex(column, row + 1);
            int bottomRight = GetIndex(column + 1, row + 1);
            indices[next++] = topLeft; indices[next++] = bottomLeft; indices[next++] = topRight;
            indices[next++] = topRight; indices[next++] = bottomLeft; indices[next++] = bottomRight;
            indices[next++] = topRight; indices[next++] = bottomLeft; indices[next++] = topLeft;
            indices[next++] = bottomRight; indices[next++] = bottomLeft; indices[next++] = topRight;
        }
        return indices;
    }
}
