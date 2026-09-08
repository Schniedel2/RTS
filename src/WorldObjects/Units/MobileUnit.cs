using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace RTS;

public class MobileUnit : Unit
{
    public int _pathRequestId;
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
    public virtual float BuildRate => 0.0f;
    public IMovementProfile MovementProfile { get; }
    public Guid? CurrentConstructionSiteId { get; private set; }
    public bool IsBuilding { get; private set; }
    public IReadOnlyList<Point> PlannedPath => _plannedPath;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Goto, "Goto", 0, 0),
        new(UnitActionType.Stop, "Stop", 2, 0)
    ];

    private readonly List<Point> _plannedPath = [];
    private double _nextAttackReplanTime;
    private Vector2? _lastAttackApproachTarget;

    public MobileUnit(
        Vector3 position,
        int length,
        int width,
        float height,
        Guid unitId,
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            length,
            width,
            height,
            unitId)
    {
        MovementProfile = movementProfile ?? new GroundMovementProfile();
    }

    public void AlignToTerrain()
    {
        Transform = CreateTerrainTransform(Globals.World.Terrain);
    }

    public void AlignToTerrain(GameTime gameTime)
    {
        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Velocity += Vector3.Down * Gravity * deltaTime;

        Vector3 nextPosition = Position + Velocity * deltaTime;
        Matrix transform = Transform;
        transform.Translation = nextPosition;
        Transform = transform;

        Matrix terrainTransform = CreateTerrainTransform(Globals.World.Terrain);
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

    protected virtual void MoveAlongPath(GameTime gameTime)
    {
        if (_plannedPath.Count == 0)
            return;

        Point nextCell = _plannedPath[0];
        Point currentCell = Globals.World.GameGrid.ToCell(Position);

        if (HeadingSnapAngle > 0.0f && currentCell == nextCell)
        {
            CompleteWaypoint();
            return;
        }

        Vector3 target = new(nextCell.X + 0.5f, Position.Y, (nextCell.Y + 0.5f));
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
            TryMoveTo(Position + desiredDirection * movementDistance);
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

        TryMoveTo(Position + forward * movementDistance);
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

    protected void CompleteWaypoint()
    {
        Point completedWaypoint = _plannedPath[0];
        _plannedPath.RemoveAt(0);
        PathDebug($"waypoint reached cell=({completedWaypoint.X},{completedWaypoint.Y}) remaining={_plannedPath.Count}");

        if (_plannedPath.Count == 0)
        {
            if (CurrentConstructionSiteId is not null)
            {
                IsBuilding = true;
                PathDebug("arrived at construction site; building starts");
                return;
            }

            PathDebug("destination reached; command completed");
            ClearCommand();
        }
    }

    private static Vector3 GetPathDirection(Point from, Point to)
    {
        Vector3 direction = new(to.X - from.X, 0.0f, to.Y - from.Y);

        return Vector3.Normalize(direction);
    }

    protected bool TryMoveTo(Vector3 position)
    {
        Point targetCell = Globals.World.GameGrid.ToCell(position);

        if (!Globals.World.GameGrid.TryMove(this, targetCell))
        {
            PathDebug($"movement blocked at cell=({targetCell.X},{targetCell.Y})");
            return false;
        }

        SetPosition(position);
        return true;
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
        CurrentConstructionSiteId = null;
        IsBuilding = false;
        CurrentCommand = command;

        _plannedPath.Clear();
        PathDebug($"path search requested target=({command.Target.X:0.0},{command.Target.Y:0.0}) request={_pathRequestId}");

        map.PathfindingManager.RequestPath(
            this,
            MovementProfile,
            command.Target, 
            _pathRequestId);

        return true;
    }

    public virtual bool TryReceiveBuildConstructionCommand(
        GameWorld map,
        ConstructionSite constructionSite)
    {
        Vector3 sitePosition = constructionSite.Position;
        float approachDistance = Math.Max(constructionSite.Length, constructionSite.Width) * 0.5f +
            Math.Max(Length, Width) * 0.5f + 1.0f;

        Vector2[] offsets =
        [
            new(-approachDistance, 0.0f), new(approachDistance, 0.0f),
            new(0.0f, -approachDistance), new(0.0f, approachDistance),
            new(-approachDistance, -approachDistance), new(approachDistance, -approachDistance),
            new(-approachDistance, approachDistance), new(approachDistance, approachDistance)
        ];

        foreach (Vector2 offset in offsets)
        {
            Vector2 target = new(sitePosition.X + offset.X, sitePosition.Z + offset.Y);
            Point targetCell = map.GameGrid.ToCell(new Vector3(target.X, 0.0f, target.Y));
            if (!map.GameGrid.CanPlace(this, targetCell))
                continue;

            bool accepted = TryReceiveGotoCommand(map, new GotoCommand(target));
            if (accepted)
                CurrentConstructionSiteId = constructionSite.UnitId;

            return accepted;
        }

        return false;
    }

    public void SetPlannedPath(IReadOnlyList<Point> path)
    {
        _plannedPath.Clear();
        _plannedPath.AddRange(path);
        PathDebug($"path applied waypoints={_plannedPath.Count}");
    }

    public override void ClearCommand()
    {
        if (CurrentCommand is not null || _plannedPath.Count > 0)
            PathDebug($"command cleared remainingWaypoints={_plannedPath.Count}");
        _plannedPath.Clear();
        CurrentConstructionSiteId = null;
        IsBuilding = false;
        base.ClearCommand();
    }

    public override void Update(GameTime gameTime)
    {
        UpdateAttackMovement(gameTime);
        MoveAlongPath(gameTime);
        AlignToTerrain(gameTime);
    }

    private void UpdateAttackMovement(GameTime gameTime)
    {
        Vector2? targetPosition = null;
        MobileUnit? targetUnit = null;
        if (AttackTargetId is Guid targetId)
        {
            targetUnit = Globals.World.Units.FindById(targetId);
            if (targetUnit is null)
                return;
            targetPosition = new(targetUnit.Position.X, targetUnit.Position.Z);
        }
        else if (AttackGroundTarget is Vector3 groundTarget)
        {
            targetPosition = new(groundTarget.X, groundTarget.Z);
        }

        if (targetPosition is not Vector2 target)
            return;

        Vector2 position = new(Position.X, Position.Z);
        if (Vector2.DistanceSquared(position, target) <= AttackRange * AttackRange)
        {
            if (CurrentCommand is not null)
                ClearCommand();
            _lastAttackApproachTarget = null;
            PathDebug("attack target is in range; approach path cleared");
            return;
        }

        if (gameTime.TotalGameTime.TotalSeconds < _nextAttackReplanTime)
            return;

        Vector2 approachTarget = target;
        if (targetUnit is not null)
        {
            Vector2 away = position - target;
            if (away.LengthSquared() <= 0.001f)
                away = Vector2.UnitX;
            else
                away.Normalize();
            float distance = MathF.Max(AttackRange * 0.75f,
                MathF.Max(targetUnit.Length, targetUnit.Width) * 0.5f +
                MathF.Max(Length, Width) * 0.5f + 1.0f);
            approachTarget = target + away * distance;
        }

        bool needsReplan = CurrentCommand is null ||
            _lastAttackApproachTarget is null ||
            Vector2.DistanceSquared(approachTarget, _lastAttackApproachTarget.Value) > 4.0f;
        if (needsReplan)
        {
            PathDebug($"attack replan target=({approachTarget.X:0.0},{approachTarget.Y:0.0})");
            if (TryReceiveGotoCommand(Globals.World, new GotoCommand(approachTarget)))
                _lastAttackApproachTarget = approachTarget;
        }
        _nextAttackReplanTime = gameTime.TotalGameTime.TotalSeconds + 0.5;
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
            int terrainX = (int)MathF.Floor(worldPosition.X);
            int terrainZ = (int)MathF.Floor(worldPosition.Z);

        return new Vector3(
            worldPosition.X,
            terrain.GetHeight(terrainX, terrainZ),
            worldPosition.Z);
    }

    protected Vector3 GetHorizontalDirection(Vector3 localDirection)
        {
        Vector3 worldDirection = Vector3.TransformNormal(
            localDirection,
                Transform);
        worldDirection.Y = 0.0f;

        return Vector3.Normalize(worldDirection);
    }

    protected void PathDebug(string message)
    {
        if (Globals.Debug_ShowPathfindingMessages)
            Globals.Console.Print($"[PATH] unit={UnitId.ToString("N")[..8]} {message}");
    }
}
