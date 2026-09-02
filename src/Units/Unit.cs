using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class Unit : WorldObject
{
    private static readonly VertexPositionColorNormal[] MeshVertices =
    [
        new(new Vector3(-0.5f, 0.0f, -0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(0.5f, 0.0f, -0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(0.5f, 0.0f, 0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(-0.5f, 0.0f, 0.5f), Color.SteelBlue, Vector3.Down),
        new(new Vector3(-0.5f, 1.0f, -0.5f), Color.SteelBlue, Vector3.Up),
        new(new Vector3(0.5f, 1.0f, -0.5f), Color.SteelBlue, Vector3.Up),
        new(new Vector3(0.5f, 1.0f, 0.5f), Color.SteelBlue, Vector3.Up),
        new(new Vector3(-0.5f, 1.0f, 0.5f), Color.SteelBlue, Vector3.Up),
    ];

    private static readonly int[] MeshIndices =
    [
        0, 2, 1, 0, 3, 2,
        4, 5, 6, 4, 6, 7,
        0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5,
        2, 3, 7, 2, 7, 6,
        3, 0, 4, 3, 4, 7,
    ];

    public int _pathRequestId;
    public Guid UnitId { get; }
    protected override VertexPositionColorNormal[] Vertices => MeshVertices;
    protected override int[] Indices => MeshIndices;

    public int Length { get; }
    public int Width { get; }
    public float Height { get; }
    public float MoveSpeed { get; set; } = 8.0f;
    public float RotationSpeed { get; set; } = MathHelper.Pi;
    public float WaypointArrivalRadius { get; set; } = 0.2f;
    public float ForwardMovementDotThreshold { get; set; } = 0.9f;
    public float HeadingSnapAngle { get; set; }
    public bool CanOnlyMoveForward { get; set; }
    public bool CanTurnInPlace { get; set; }
    public Vector3 Velocity { get; private set; }
    public float Gravity { get; set; } = 10.0f;
    public float Bounciness { get; set; } = 0.25f;
    public float RestingSpeed { get; set; } = 1.0f;
    public bool IsSelected { get; set; }
    public GotoCommand? CurrentCommand { get; private set; }
    public IMovementProfile MovementProfile { get; }
    public IReadOnlyList<Point> PlannedPath => _plannedPath;
    public virtual IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 0)
    ];

    private readonly List<Point> _plannedPath = [];

    public Unit(
        GraphicsDevice graphicsDevice,
        Vector3 position,
        int length,
        int width,
        float height,
        IMovementProfile? movementProfile = null,
        Guid? unitId = null) : base(graphicsDevice, position)
    {
        UnitId = unitId ?? Guid.NewGuid();
        Length = length;
        Width = width;
        Height = height;
        MovementProfile = movementProfile ?? new GroundMovementProfile();
    }

    public void SetVelocity(Vector3 velocity)
    {
        Velocity = velocity;
    }

    public void ReceiveCommand(GotoCommand command)
    {
        CurrentCommand = command;
        _pathRequestId++;
    }

    public virtual bool TryReceiveGotoCommand(
        GameWorld map,
        GotoCommand command)
    {
        CurrentCommand = command;

        _plannedPath.Clear();

        map.PathfindingManager.RequestPath(
            this,
            MovementProfile,
            command.Target, 
            _pathRequestId);

        return true;
    }

    public void SetPlannedPath(IReadOnlyList<Point> path)
    {
        _plannedPath.Clear();
        _plannedPath.AddRange(path);
    }

    public void ClearCommand()
    {
        CurrentCommand = null;
        _plannedPath.Clear();
    }

    public void AlignToTerrain(Terrain terrain)
    {
        Transform = CreateTerrainTransform(terrain);
    }

    public void AlignToTerrain(Terrain terrain, GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Velocity += Vector3.Down * Gravity * deltaTime;

        Vector3 nextPosition = Position + Velocity * deltaTime;
        Matrix transform = Transform;
        transform.Translation = nextPosition;
        Transform = transform;

        Matrix terrainTransform = CreateTerrainTransform(terrain);
        float targetHeight = terrainTransform.Translation.Y;

        if (nextPosition.Y > targetHeight)
            return;

        Transform = terrainTransform;

        Vector3 terrainNormal = Transform.Up;
        float impactSpeed = Vector3.Dot(Velocity, terrainNormal);

        if (impactSpeed >= 0.0f)
            return;

        Velocity = Vector3.Reflect(Velocity, terrainNormal) * Bounciness;

        if (Velocity.LengthSquared() <= RestingSpeed * RestingSpeed)
            Velocity = Vector3.Zero;
    }

    public void Update(GameTime gameTime, Terrain terrain, GameWorld map)
    {
        MoveAlongPath(gameTime, terrain, map);
        AlignToTerrain(terrain, gameTime);
    }

    protected virtual void MoveAlongPath(
        GameTime gameTime,
        Terrain terrain,
        GameWorld map)
        {
            if (_plannedPath.Count == 0)
                return;

            Point nextCell = _plannedPath[0];
            Point currentCell = map.Grid.ToCell(Position);

            if (HeadingSnapAngle > 0.0f && currentCell == nextCell)
            {
                CompleteWaypoint();
                return;
            }

            Vector3 target = new(
                (nextCell.X + 0.5f) * terrain.CellSize,
                Position.Y,
                (nextCell.Y + 0.5f) * terrain.CellSize);
            Vector3 toTarget = target - Position;
            toTarget.Y = 0.0f;

            float distanceToTarget = toTarget.Length();
            float movementDistance = MoveSpeed *
                (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (distanceToTarget <= WaypointArrivalRadius)
            {
                 CompleteWaypoint();
                return;
            }

            Vector3 desiredDirection = toTarget / distanceToTarget;

            if (!CanOnlyMoveForward)
            {
                 FaceDirection(desiredDirection);
                 TryMoveTo(map, Position + desiredDirection * movementDistance);
                return;
            }

            Vector3 forward = GetHorizontalDirection(Vector3.Forward);
              Vector3 steeringDirection = desiredDirection;

              if (HeadingSnapAngle > 0.0f)
                steeringDirection = GetPathDirection(currentCell, nextCell);

            if (CanTurnInPlace &&
                 Vector3.Dot(forward, steeringDirection) < ForwardMovementDotThreshold)
            {
                 TurnTowards(steeringDirection, gameTime);
                return;
            }

            if (!CanTurnInPlace)
                 forward = TurnTowards(steeringDirection, gameTime);

            TryMoveTo(map, Position + forward * movementDistance);
        }

    protected Vector3 TurnTowards(Vector3 desiredDirection, GameTime gameTime)
        {
            Vector3 currentForward = GetHorizontalDirection(Vector3.Forward);
            float turnAngle = MathF.Atan2(
                Vector3.Cross(currentForward, desiredDirection).Y,
                Vector3.Dot(currentForward, desiredDirection));
            float maximumTurn = RotationSpeed *
                (float)gameTime.ElapsedGameTime.TotalSeconds;
            float appliedTurn = MathHelper.Clamp(
                turnAngle,
                -maximumTurn,
                maximumTurn);

            Transform = Matrix.CreateRotationY(appliedTurn) * Transform;

            return GetHorizontalDirection(Vector3.Forward);
        }

    protected void FaceDirection(Vector3 desiredDirection)
    {
        Vector3 forward = desiredDirection;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.Up));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        Transform = new Matrix(
            right.X, right.Y, right.Z, 0.0f,
            up.X, up.Y, up.Z, 0.0f,
            -forward.X, -forward.Y, -forward.Z, 0.0f,
            Position.X, Position.Y, Position.Z, 1.0f);
    }

    private Vector3 SnapDirectionToHeading(Vector3 direction)
    {
        float heading = MathF.Atan2(-direction.X, -direction.Z);
        float snappedHeading = MathF.Round(heading / HeadingSnapAngle) *
            HeadingSnapAngle;

        return Vector3.Transform(
            Vector3.Forward,
            Matrix.CreateRotationY(snappedHeading));
    }

        private void CompleteWaypoint()
        {
            _plannedPath.RemoveAt(0);

            if (_plannedPath.Count == 0)
                CurrentCommand = null;
        }

        private static Vector3 GetPathDirection(Point from, Point to)
        {
            Vector3 direction = new(to.X - from.X, 0.0f, to.Y - from.Y);

            return Vector3.Normalize(direction);
        }

        protected bool TryMoveTo(GameWorld map, Vector3 position)
        {
            Point targetCell = map.Grid.ToCell(position);

            if (!map.Grid.TryMove(this, targetCell))
                return false;

            SetPosition(position);
            return true;
        }

    public Rectangle GetScreenBounds(
        Matrix view,
        Matrix projection,
        Viewport viewport)
    {
        Matrix world = GetWorldMatrix();
        Point minimum = new(int.MaxValue, int.MaxValue);
        Point maximum = new(int.MinValue, int.MinValue);

        foreach (VertexPositionColorNormal vertex in Vertices)
        {
            Vector3 screenPosition = viewport.Project(
                vertex.Position,
                projection,
                view,
                world);
                int screenX = (int)screenPosition.X;
                int screenY = (int)screenPosition.Y;
                minimum.X = Math.Min(minimum.X, screenX);
                minimum.Y = Math.Min(minimum.Y, screenY);
                maximum.X = Math.Max(maximum.X, screenX);
                maximum.Y = Math.Max(maximum.Y, screenY);
        }

        return new Rectangle(
            minimum.X,
            minimum.Y,
            maximum.X - minimum.X + 1,
            maximum.Y - minimum.Y + 1);
    }

    private Matrix CreateTerrainTransform(Terrain terrain)
    {
        float halfWidth = Width * 0.5f;
        float halfLength = Length * 0.5f;

            Vector3 horizontalRight = GetHorizontalDirection(Vector3.Right);
            Vector3 horizontalForward = GetHorizontalDirection(Vector3.Forward);
            Vector3 position = Position;

            Vector3 frontLeft = GetTerrainPoint(
                terrain,
                position - horizontalRight * halfWidth + horizontalForward * halfLength);
            Vector3 frontRight = GetTerrainPoint(
                terrain,
                position + horizontalRight * halfWidth + horizontalForward * halfLength);
            Vector3 backLeft = GetTerrainPoint(
                terrain,
                position - horizontalRight * halfWidth - horizontalForward * halfLength);
            Vector3 backRight = GetTerrainPoint(
                terrain,
                position + horizontalRight * halfWidth - horizontalForward * halfLength);

            Vector3 rightTangent =
                (frontRight - frontLeft + backRight - backLeft) * 0.5f;
            Vector3 backTangent =
                (backLeft - frontLeft + backRight - frontRight) * 0.5f;
            Vector3 up = Vector3.Normalize(Vector3.Cross(backTangent, rightTangent));

            if (Vector3.Dot(up, Vector3.Up) < 0.0f)
                up = -up;

            Vector3 right = Vector3.Normalize(rightTangent);
            Vector3 back = Vector3.Normalize(Vector3.Cross(right, up));
            Vector3 terrainCenter =
                (frontLeft + frontRight + backLeft + backRight) * 0.25f;

            return new Matrix(
                right.X, right.Y, right.Z, 0.0f,
                up.X, up.Y, up.Z, 0.0f,
                back.X, back.Y, back.Z, 0.0f,
                terrainCenter.X, terrainCenter.Y, terrainCenter.Z, 1.0f);
    }

    protected override Matrix GetWorldMatrix()
    {
        return Matrix.CreateScale(Width, Height, Length) *
                Transform;
    }

    private static Vector3 GetTerrainPoint(
        Terrain terrain,
            Vector3 worldPosition)
    {
            int terrainX = (int)MathF.Floor(worldPosition.X / terrain.CellSize);
            int terrainZ = (int)MathF.Floor(worldPosition.Z / terrain.CellSize);

        return new Vector3(
            worldPosition.X,
            terrain.GetHeight(terrainX, terrainZ),
            worldPosition.Z);
    }

    private Vector3 GetHorizontalDirection(Vector3 localDirection)
        {
        Vector3 worldDirection = Vector3.TransformNormal(
            localDirection,
                Transform);
        worldDirection.Y = 0.0f;

        return Vector3.Normalize(worldDirection);
    }

    public void Select(bool isSelected = true)
    {
        IsSelected = isSelected;
    }    
}