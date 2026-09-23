using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>Current wheel roll angle in degrees, normalized to -180..+180.</summary>
    public float WheelRotationDegrees { get; private set; }
    /// <summary>Wheel radius in world units, used to convert travelled distance into rotation.</summary>
    public float WheelRadius { get; set; } = 0.35f;
    public virtual float BuildRate => 0.0f;
    public IMovementProfile MovementProfile { get; }
    public Guid? TargetBuildingId { get; private set; }
    public bool IsBuilding { get; private set; }
    public Guid? PendingEnterContainerId { get; private set; }
    /// <summary>
    /// True while a freshly produced unit moves from an interior spawn pivot
    /// to the building's exterior exit pivot without occupying the GameGrid.
    /// </summary>
    public bool IsLeavingBuilding { get; private set; }
    public Guid? SpawnSourceBuildingId { get; private set; }
    public Vector3 SpawnExitPosition { get; private set; }
    private Vector2? _productionRallyPoint;

    internal void SetProductionRallyPoint(RallyPointState? state) =>
        _productionRallyPoint = state is { HasPosition: true } point ? new Vector2(point.X, point.Z) : null;

    private void MoveToProductionRallyPoint()
    {
        Vector2? target = _productionRallyPoint;
        _productionRallyPoint = null;
        if (target is not Vector2 position || CurrentCommand is not null)
            return;

        GameWorld world = Globals.World;
        Point center = world.GameGrid.ToCell(new Vector3(position.X, 0, position.Y));
        // Earlier recruits may already occupy the marker. Gather around it
        // instead of abandoning every subsequent order at the building exit.
        for (int radius = 0; radius <= 6; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                        continue;
                    Point cell = center + new Point(x, y);
                    if (!MovementProfile.CanEnter(world, this, cell) || !world.GameGrid.IsPathfindingAllowed(this, cell))
                        continue;
                    Vector3 cellPosition = world.GameGrid.ToWorldPosition(cell, 0);
                    Vector2 destination = radius == 0 ? position : new Vector2(cellPosition.X, cellPosition.Z);
                    TryReceiveGotoCommand(world, new GotoCommand(destination));
                    return;
                }
            }
        }
    }
    public override bool IsSelectable => base.IsSelectable && !IsLeavingBuilding;
    public override bool CanBeTargeted => base.CanBeTargeted && !IsLeavingBuilding;
    public IReadOnlyList<Point> PlannedPath => _plannedPath;
    public override IReadOnlyList<UnitAction> Actions =>
    [
        new(UnitActionType.Scouting, "AI: Scouting", 5, 1),
        new(UnitActionType.MoveAway, "AI: Move away", 4, 1),
        new(UnitActionType.Goto, "Goto", 0, 0),
        new(UnitActionType.Follow, "Follow", 6, 1),
        new(UnitActionType.Stop, "Stop", 2, 0)
    ];

    private readonly List<Point> _plannedPath = [];
    private readonly Queue<(GotoCommand Command, Point[]? Route)> _commandQueue = [];
    public Vector2 LastQueuedTarget => _commandQueue is { Count: > 0 }
        ? _commandQueue.Last().Command.Target
        : CurrentCommand?.Target ?? new Vector2(Position.X, Position.Z);
    private double _nextAttackReplanTime;
    private Vector2? _lastAttackApproachTarget;
    private double _nextFollowReplanTime;
    private Vector2? _lastFollowApproachTarget;
    private bool _followPathActive;

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

            Matrix previousTransform = Transform;
            Transform = Matrix.CreateRotationY(appliedTurn) * Transform;
            // At the 45° threshold the grid footprint may switch from
            // Width×Length to Length×Width. Do not visually turn into cells
            // which are blocked by another unit or a building.
            if (!Globals.World.GameGrid.TryUpdateFootprint(this))
                Transform = previousTransform;

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
            if (PendingEnterContainerId is not null)
            {
                CurrentCommand = null;
                PathDebug("container entrance reached; waiting for host embark command");
                return;
            }

            if (TargetBuildingId is not null)
            {
                IsBuilding = true;
                PathDebug("arrived at construction site; building starts");
                return;
            }

            PathDebug("destination reached; command completed");
            if (_commandQueue is { Count: > 0 })
            {
                var queued = _commandQueue.Dequeue();
                StartGoto(Globals.World, queued.Command, queued.Route);
            }
            else
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
        GameGrid grid = Globals.World.GameGrid;
        Point targetCell = grid.ToCell(position);
        Point currentCell = grid.ToCell(Position);

        // Continuous visual movement within an already occupied cell cannot
        // change the discrete footprint. Ask the grid only when crossing into
        // another cell; body turns are handled separately by TurnTowards.
        if (targetCell != currentCell && !grid.TryMove(this, targetCell))
        {
            PathDebug($"movement blocked at cell=({targetCell.X},{targetCell.Y})");
            return false;
        }

        AdvanceWheelRotation(position);
        SetPosition(position);
        return true;
    }

    public void BeginLeavingBuilding(Guid sourceBuildingId, Vector3 exitPosition)
    {
        Stop();
        SpawnSourceBuildingId = sourceBuildingId;
        SpawnExitPosition = exitPosition;
        IsLeavingBuilding = true;
        _currentUnitState = UnitActionState.Spawning;
        OnBeginLeavingBuilding();
        PathDebug($"leaving building={sourceBuildingId.ToString("N")[..8]} exit=({exitPosition.X:0.0},{exitPosition.Z:0.0})");
    }

    /// <summary>Lets animated units immediately enter their spawn/run animation.</summary>
    protected virtual void OnBeginLeavingBuilding()
    {
    }

    /// <summary>Lets animated units return to idle after reaching the exit.</summary>
    protected virtual void OnFinishedLeavingBuilding()
    {
    }

    /// <summary>
    /// Moves along the authored interior-to-exterior corridor. The unit is not
    /// registered in the grid until it reaches a genuinely free exit cell, so
    /// it cannot overwrite the production building's footprint.
    /// </summary>
    private bool UpdateLeavingBuilding(GameTime gameTime)
    {
        if (!IsLeavingBuilding)
            return false;

        Vector3 toExit = SpawnExitPosition - Position;
        Vector3 horizontal = new(toExit.X, 0.0f, toExit.Z);
        float distance = horizontal.Length();
        float movementDistance = MoveSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (distance > 0.0001f)
            FaceDirection(horizontal / distance);

        if (distance > Math.Max(WaypointArrivalRadius, movementDistance))
        {
            float fraction = movementDistance / distance;
            Vector3 nextPosition = Position + horizontal / distance * movementDistance;
            nextPosition.Y = MathHelper.Lerp(Position.Y, SpawnExitPosition.Y, fraction);
            AdvanceWheelRotation(nextPosition);
            SetPosition(nextPosition);
            return true;
        }

        Point exitCell = Globals.World.GameGrid.ToCell(SpawnExitPosition);
        if (!Globals.World.GameGrid.TryMove(this, exitCell))
        {
            PathDebug($"building exit blocked cell=({exitCell.X},{exitCell.Y}); waiting");
            return true;
        }

        AdvanceWheelRotation(SpawnExitPosition);
        SetPosition(SpawnExitPosition);
        IsLeavingBuilding = false;
        SpawnSourceBuildingId = null;
        _currentUnitState = UnitActionState.Idle;
        OnFinishedLeavingBuilding();
        MoveToProductionRallyPoint();
        PathDebug("building exit reached; normal grid movement enabled");
        return true;
    }

    /// <summary>
    /// Applies the standard vehicle animation parameters to a Blockbench mesh.
    /// Mesh parameters use radians; Unit state uses degrees.
    /// </summary>
    protected void ApplyMeshAnimationParameters(Mesh mesh)
    {
        mesh.SetParameter(Mesh.WheelAngle, -MathHelper.ToRadians(WheelRotationDegrees));
    }

    protected void AdvanceWheelRotation(Vector3 nextPosition)
    {
        if (WheelRadius <= 0.0f)
            return;

        Vector3 movement = nextPosition - Position;
        movement.Y = 0.0f;
        float distance = movement.Length();
        if (distance <= 0.0001f)
            return;

        Vector3 forward = GetHorizontalDirection(Vector3.Forward);
        float direction = Vector3.Dot(movement, forward) < 0.0f ? -1.0f : 1.0f;
        float rotationDegrees = MathHelper.ToDegrees(distance / WheelRadius) * direction;
        WheelRotationDegrees = WrapDegrees(WheelRotationDegrees + rotationDegrees);
    }

    private static float WrapDegrees(float degrees)
    {
        while (degrees > 180.0f)
            degrees -= 360.0f;
        while (degrees < -180.0f)
            degrees += 360.0f;
        return degrees;
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
        GotoCommand command,
        bool appendToQueue = false,
        IReadOnlyList<Point>? route = null)
    {
        if (appendToQueue && (CurrentCommand is not null || _plannedPath.Count > 0))
        {
            _commandQueue.Enqueue((command, route?.ToArray()));
            return true;
        }
        _commandQueue.Clear();
        return StartGoto(map, command, route);
    }

    private bool StartGoto(GameWorld map, GotoCommand command, IReadOnlyList<Point>? route = null)
    {
        _productionRallyPoint = null;
        PendingEnterContainerId = null;
        TargetBuildingId = null;
        IsBuilding = false;
        CurrentCommand = command;

        _plannedPath.Clear();
        PathDebug($"path search requested target=({command.Target.X:0.0},{command.Target.Y:0.0}) request={_pathRequestId}");

        if (route is not null)
        {
            SetPlannedPath(route);
            return true;
        }
        map.PathfindingManager.RequestPath(
            this,
            MovementProfile,
            command.Target, 
            _pathRequestId);

        return true;
    }

    public bool TryReceiveEnterUnitCommand(GameWorld map, Unit container)
    {
        container.TryGetEntryWorldPosition(out Vector3 entrancePosition);
        entrancePosition.Y = 0.0f;
        bool accepted = TryReceiveGotoCommand(
            map,
            new GotoCommand(new Vector2(entrancePosition.X, entrancePosition.Z)));
        if (accepted)
            PendingEnterContainerId = container.UnitId;
        return accepted;
    }

    internal void ClearPendingEnterContainer() => PendingEnterContainerId = null;

    public virtual bool TryReceiveBuildConstructionCommand(
        GameWorld map,
        Building constructionSite)
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
                TargetBuildingId = constructionSite.UnitId;

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
        _commandQueue?.Clear();
        if (CurrentCommand is not null || _plannedPath.Count > 0)
            PathDebug($"command cleared remainingWaypoints={_plannedPath.Count}");
        _plannedPath.Clear();
        if (PendingEnterContainerId is Guid containerId &&
            Globals.World.Units.FindById(containerId) is Unit container)
        {
            container.Occupancy?.ClearReservation(UnitId);
        }
        _productionRallyPoint = null;
        PendingEnterContainerId = null;
        TargetBuildingId = null;
        IsBuilding = false;
        base.ClearCommand();
    }

    protected void UpdateUnitVisuals(GameTime gameTime) => base.Update(gameTime);

    public override void Update(GameTime gameTime)
    {
        if (UpdateLeavingBuilding(gameTime))
        {
            base.Update(gameTime);
            return;
        }

        UpdateFollowMovement(gameTime);
        UpdateAttackMovement(gameTime);
        MoveAlongPath(gameTime);
        AlignToTerrain(gameTime);
        base.Update(gameTime);
    }

    private void UpdateAttackMovement(GameTime gameTime)
    {
        Vector2? targetPosition = null;
        if (AttackGroundTarget is Vector3 groundTarget)
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

    private void UpdateFollowMovement(GameTime gameTime)
    {
        if (FollowUnitId is not Guid followId)
            return;

        Unit? followUnit = Globals.World.Units.FindById(followId);
        if (followUnit is null || followUnit == this)
        {
            ClearFollowUnit();
            if (_followPathActive)
                ClearCommand();
            _followPathActive = false;
            return;
        }

        Vector2 position = new(Position.X, Position.Z);
        Vector2 target = new(followUnit.Position.X, followUnit.Position.Z);
        float followDistance = FollowDistance;
        if (Vector2.DistanceSquared(position, target) <= followDistance * followDistance)
        {
            if (_followPathActive)
                ClearCommand();
            _followPathActive = false;
            _lastFollowApproachTarget = null;
            return;
        }

        if (gameTime.TotalGameTime.TotalSeconds < _nextFollowReplanTime)
            return;

        Vector2 away = position - target;
        if (away.LengthSquared() <= 0.001f)
            away = Vector2.UnitX;
        else
            away.Normalize();
        Vector2 approachTarget = target + away * followDistance;
        bool needsReplan = !_followPathActive || CurrentCommand is null ||
            _lastFollowApproachTarget is null ||
            Vector2.DistanceSquared(approachTarget, _lastFollowApproachTarget.Value) > 4.0f;
        if (needsReplan && TryReceiveGotoCommand(Globals.World, new GotoCommand(approachTarget)))
        {
            _followPathActive = true;
            _lastFollowApproachTarget = approachTarget;
            PathDebug($"follow replan target=({approachTarget.X:0.0},{approachTarget.Y:0.0}) distance={followDistance:0.0}");
        }
        _nextFollowReplanTime = gameTime.TotalGameTime.TotalSeconds + 0.5;
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

    public override Matrix GetWorldMatrix()
    {
        return Matrix.CreateScale(1.0f) * Transform;
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
