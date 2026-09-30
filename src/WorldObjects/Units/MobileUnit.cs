using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RTS.Network;

namespace RTS;

public sealed record MobileUnitState(float X, float Y, float Z, float YawDegrees, bool IsMoving,
    Guid? SquadLeaderId = null, GroundNavigationState? Navigation = null,
    int PathProgress = 0, long NavigationRevision = 0);

public enum MovementStatus { Idle, FollowingRoute, Waiting, Planning, Blocked }
public sealed record QueuedMovementState(float X, float Z, Point[]? Route);
public sealed record GroundNavigationState(float? TargetX, float? TargetZ, Point[] Route,
    QueuedMovementState[] Queue, Guid? ConstructionSiteId, Guid? ContainerId, bool IsBuilding,
    MovementStatus Status, Point? PreviousCell = null);


public class MobileUnit : Unit
{
    // CanTurnInPlace is an additional capability, not a driving restriction.
    public Guid? SquadLeaderId { get; internal set; }
    public override string StateTypeId => "mobile-unit-state";
    public int _pathRequestId;
    public float MoveSpeed { get; set; } = 8.0f;
    public float RotationSpeed { get; set; } = MathHelper.Pi;
    public float WaypointArrivalRadius { get; set; } = 0.2f;
    /// <summary>Optional driving data for forward-moving ground vehicles only.</summary>
    public GroundSteeringProfile? GroundSteering { get; set; }
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
        if (!IsMovementAuthority) return;
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
    private Point? _steeringWaypoint;
    private Point? _steeringRouteHead;
    private readonly Queue<(GotoCommand Command, Point[]? Route)> _commandQueue = [];
    public Vector2 LastQueuedTarget => _commandQueue is { Count: > 0 }
        ? _commandQueue.Last().Command.Target
        : CurrentCommand?.Target ?? new Vector2(Position.X, Position.Z);
    private double _nextAttackReplanTime;
    private Vector2? _lastAttackApproachTarget;
    private double _nextFollowReplanTime;
    private Vector2? _lastFollowApproachTarget;
    private bool _followPathActive;
    private float _blockedMovementSeconds;
    private Point? _progressWaypoint;
    private float _bestWaypointDistanceSquared = float.PositiveInfinity;
    private float _withoutPathProgressSeconds;
    public const float MovementStallTimeoutSeconds = 2.0f;
    private const float MinimumProgressDistance = 0.05f;
    private const float BlockedMovementGraceSeconds = MovementStallTimeoutSeconds;

    // Offline worlds are authoritative too; connected clients only predict confirmed routes.
    internal static bool IsMovementAuthority =>
        Globals.Game?.Network is not { IsConnected: true, IsHost: false };
    public MovementStatus MovementStatus { get; private set; }
    private float _retryMovementSeconds;
    private int _movementRetryCount;
    private bool _turningTowardPath;
    private Point? _previousRouteCell;
    private int _pathProgress;
    private long _navigationRevision;
    private long _publishedNavigationRevision = -1;
    private long _replicatedNavigationRevision = -1;
    private Point[] _replicatedRoute = [];
    private int _replicatedRouteStartProgress;
    private Point? _replicatedPreviousCell;
    private Vector3 _renderCorrectionOffset;
    private float _renderCorrectionYaw;
    internal double NextNavigationHeartbeat { get; set; }
    internal bool HasUnpublishedNavigation => _navigationRevision != _publishedNavigationRevision;
    internal long NavigationRevision => _navigationRevision;
    internal void MarkNavigationPublished(long revision) => _publishedNavigationRevision = revision;
    private void NavigationChanged() => _navigationRevision++;

    public override string GetDebugCommandText()
    {
        if (IsLeavingBuilding)
            return $"Leaving building -> ({SpawnExitPosition.X:0.0}, {SpawnExitPosition.Z:0.0})";
        if (PendingEnterContainerId is Guid containerId)
            return $"Enter {containerId.ToString("N")[..8]} | path={_plannedPath.Count}";
        if (IsBuilding && TargetBuildingId is Guid buildingId)
            return $"Construct {buildingId.ToString("N")[..8]}";
        if (CurrentCommand is GotoCommand command)
            return $"Goto ({command.Target.X:0.0}, {command.Target.Y:0.0}) | path={_plannedPath.Count} queue={_commandQueue.Count} movement={MovementStatus} retries={_movementRetryCount}";
        string commandText = base.GetDebugCommandText();
        return _commandQueue.Count > 0 ? $"{commandText} | queue={_commandQueue.Count}" : commandText;
    }

    public MobileUnit(
        Vector3 position,
        Guid unitId,
        IMovementProfile? movementProfile = null
        ) : base(
            position,
            unitId)
    {
        MovementProfile = movementProfile ?? new GroundMovementProfile();
    }

    /// <summary>
    /// Vehicles follow the terrain plane. Infantry can override this to keep
    /// its body upright while still using the terrain height.
    /// </summary>
    protected virtual bool AlignBodyToTerrain => true;
    /// <summary>
    /// Vehicle wrecks and heavy units may visibly settle after a height
    /// change. Infantry should remain planted without springing.
    /// </summary>
    protected virtual bool UseTerrainLandingPhysics => true;

    public void AlignToTerrain()
    {
        Transform = CreateTerrainTransform(Globals.World.Terrain);
    }

    public void AlignToTerrain(GameTime gameTime)
    {
        if (!UseTerrainLandingPhysics)
        {
            Transform = CreateTerrainTransform(Globals.World.Terrain);
            Velocity = Vector3.Zero;
            return;
        }

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

        // Safe lookahead may pass inside a corner and reach a later confirmed
        // route cell without touching every earlier cell center. Advance to that
        // confirmed cell, while keeping the final waypoint for precise arrival.
        int permittedIndex = _steeringWaypoint is Point selected ? _plannedPath.IndexOf(selected) : 0;
        int reachedRouteIndex = _plannedPath.IndexOf(currentCell, 0,
            Math.Min(_plannedPath.Count, Math.Max(0, permittedIndex) + 1));
        int reachedIntermediateWaypoints = Math.Min(
            reachedRouteIndex + 1,
            _plannedPath.Count - 1);
        while (reachedIntermediateWaypoints-- > 0)
        {
            CompleteWaypoint();
            if (_plannedPath.Count == 0) return;
        }
        nextCell = _plannedPath[0];

        Vector3 target = Globals.World.GameGrid.ToWorldPosition(nextCell, Position.Y);
        Vector3 toTarget = target - Position;
        toTarget.Y = 0.0f;

        float distanceToTarget = toTarget.Length();
        float movementDistance = MoveSpeed *
            (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Vehicles normally use a generous arrival radius so they do not
        // wobble around ordinary destinations. At the final construction
        // waypoint that radius could finish the route before the vehicle has
        // entered the selected approach cell and actually touches the site.
        bool isFinalWaypoint = _plannedPath.Count == 1;
        // Grid waypoints are only one cell apart. A vehicle arrival radius can
        // intentionally be larger at its final destination, but using that
        // radius for intermediate points skips entire cells and makes a large
        // footprint cut straight across building corners.
        float intermediateRadius = Math.Max(
            0.05f,
            Globals.World.GameGrid.CellSize * 0.15f);
        float arrivalRadius = !isFinalWaypoint
            ? Math.Min(WaypointArrivalRadius, intermediateRadius)
            : TargetBuildingId is not null
                ? Math.Min(WaypointArrivalRadius, 0.15f)
                : Math.Min(WaypointArrivalRadius, Globals.World.GameGrid.CellSize * 0.35f);
        if (distanceToTarget <= arrivalRadius)
        {
            CompleteWaypoint();
            return;
        }

        Vector3 steeringTarget = GetRouteSteeringTarget(target);
        Vector3 toSteeringTarget = steeringTarget - Position;
        toSteeringTarget.Y = 0;
        Vector3 desiredDirection = Vector3.Normalize(toSteeringTarget);

        if (!CanOnlyMoveForward)
        {
            FaceDirection(desiredDirection);
            movementDistance = Math.Min(movementDistance, distanceToTarget);
            TrackMovementAttempt(TryMoveTo(Position + desiredDirection * movementDistance), gameTime);
            return;
        }

        Vector3 forward = GetHorizontalDirection(Vector3.Forward);
        Vector3 steeringDirection = desiredDirection;
        GroundSteeringProfile? steering = GroundSteering;

        if (steering is { AllowReverse: true } &&
            Vector3.Dot(forward, steeringDirection) < steering.ReverseStartAlignment &&
            distanceToTarget <= steering.ClampedReverseMaximumDistance)
        {
            forward = TurnTowards(-steeringDirection, gameTime,
                steering.MovingTurnRadiansPerSecond);
            if (Vector3.Dot(-forward, steeringDirection) < steering.ReverseExitAlignment)
            {
                _turningTowardPath = true;
                return;
            }

            float reverseDistance = Math.Min(
                distanceToTarget,
                steering.ClampedReverseSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds);
            TrackMovementAttempt(TryMoveTo(Position - forward * reverseDistance), gameTime);
            return;
        }

        if (ShouldTurnInPlace(forward, steeringDirection))
        {
            TurnTowards(steeringDirection, gameTime,
                steering?.StationaryTurnRadiansPerSecond ?? RotationSpeed);
            _turningTowardPath = true;
            return;
        }

        // Keep correcting the heading while moving. Previously units which
        // could turn in place stopped steering as soon as they crossed the
        // coarse forward threshold, producing a left/right zig-zag.
        forward = TurnTowards(steeringDirection, gameTime,
            steering?.MovingTurnRadiansPerSecond ?? RotationSpeed);

        float alignment = Vector3.Dot(forward, steeringDirection);
        movementDistance *= steering?.GetCurveSpeedFactor(alignment) ?? 1.0f;
        movementDistance = Math.Min(movementDistance, distanceToTarget);
        TrackMovementAttempt(TryMoveTo(Position + forward * movementDistance), gameTime);
    }

    private bool ShouldTurnInPlace(Vector3 forward, Vector3 desiredDirection) =>
        CanTurnInPlace && Vector3.Dot(forward, desiredDirection) <
            (GroundSteering?.TurnInPlaceAlignment ?? 0.9f);

    private void TrackMovementAttempt(bool moved, GameTime gameTime)
    {
        if (moved)
        {
            _blockedMovementSeconds = 0.0f;
            if (MovementStatus != MovementStatus.FollowingRoute)
            {
                MovementStatus = MovementStatus.FollowingRoute;
                NavigationChanged();
            }
            return;
        }

        if (MovementStatus != MovementStatus.Waiting)
        {
            MovementStatus = MovementStatus.Waiting;
            NavigationChanged();
        }
        _blockedMovementSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_blockedMovementSeconds < BlockedMovementGraceSeconds)
            return;

        RequestMovementRecovery();
    }

    private Vector3 GetRouteSteeringTarget(Vector3 fallback)
    {
        if (!CanOnlyMoveForward || _plannedPath.Count == 0) return fallback;
        GameGrid grid = Globals.World.GameGrid;
        Point currentCell = grid.ToCell(Position);
        if (_steeringRouteHead == _plannedPath[0] && _steeringWaypoint is Point retained &&
            retained != currentCell && grid.CanTraverseDirect(this, currentCell, retained))
            return grid.ToWorldPosition(retained, Position.Y);

        _steeringRouteHead = _plannedPath[0];
        _steeringWaypoint = _plannedPath[0];
        int furthest = Math.Min(3, _plannedPath.Count - 1);

        // Prefer the furthest safe point on the confirmed route. This may smooth
        // a bend, but the direct corridor has to admit the complete hard footprint
        // and all diagonal side cells. The next confirmed waypoint remains the
        // fallback when a building, terrain rule or another hard occupant blocks it.
        for (int index = furthest; index > 0; index--)
            if (grid.CanTraverseDirect(this, currentCell, _plannedPath[index]))
            {
                _steeringWaypoint = _plannedPath[index];
                return grid.ToWorldPosition(_plannedPath[index], Position.Y);
            }

        return fallback;
    }

    private void ResetSteeringTarget()
    {
        _steeringWaypoint = null;
        _steeringRouteHead = null;
    }

    private void RequestMovementRecovery()
    {
        if (!IsMovementAuthority || CurrentCommand is null || MovementStatus == MovementStatus.Planning)
            return;
        ResetSteeringTarget();
        _plannedPath.Clear();
        _blockedMovementSeconds = 0;
        ResetMovementProgressWatchdog();
        _pathRequestId++;
        _movementRetryCount++;
        MovementStatus = MovementStatus.Planning;
        NavigationChanged();
        Globals.World.PathfindingManager.RequestPath(this, MovementProfile, CurrentCommand.Value.Target, _pathRequestId);
    }

    internal void OnPathSearchFailed()
    {
        if (CurrentCommand is null) return;
        _plannedPath.Clear();
        MovementStatus = MovementStatus.Blocked;
        // Bound retry frequency even for permanently unreachable orders. Keep the
        // user's task and its Shift queue until a new command or Stop replaces it.
        _retryMovementSeconds = _movementRetryCount >= 3 ? 5.0f : MovementStallTimeoutSeconds;
        NavigationChanged();
        ResetMovementProgressWatchdog();
    }

    private void UpdateMovementRecovery(float seconds)
    {
        if (CurrentCommand is null || MovementStatus != MovementStatus.Blocked) return;
        _retryMovementSeconds -= seconds;
        if (_retryMovementSeconds > 0) return;
        if (TargetBuildingId is Guid siteId && Globals.World.Units.FindById(siteId) is Building site)
        {
            _movementRetryCount++;
            TryReceiveBuildConstructionCommand(Globals.World, site, preserveQueue: true);
        }
        else RequestMovementRecovery();
    }

    protected Vector3 TurnTowards(Vector3 desiredDirection, GameTime gameTime) =>
        TurnTowards(desiredDirection, gameTime, RotationSpeed);

    protected Vector3 TurnTowards(
        Vector3 desiredDirection,
        GameTime gameTime,
        float turnRadiansPerSecond)
        {
            Vector3 currentForward = GetHorizontalDirection(Vector3.Forward);
            float turnAngle = MathF.Atan2(
                Vector3.Cross(currentForward, desiredDirection).Y,
                Vector3.Dot(currentForward, desiredDirection));
            float maximumTurn = Math.Max(0.0f, turnRadiansPerSecond) *
                (float)gameTime.ElapsedGameTime.TotalSeconds;
            float appliedTurn = MathHelper.Clamp(
                turnAngle,
                -maximumTurn,
                maximumTurn);

            Matrix previousTransform = Transform;
            Matrix candidateTransform = Matrix.CreateRotationY(appliedTurn) * Transform;
            Transform = candidateTransform;
            // Rotation updates the soft movement clearance. The hard core footprint
            // remains stable, so a visual turn cannot invalidate an accepted route.
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
        _blockedMovementSeconds = 0.0f;
        Point completedWaypoint = _plannedPath[0];
        _plannedPath.RemoveAt(0);
        _previousRouteCell = completedWaypoint;
        _pathProgress++;
        _movementRetryCount = 0;
        PathDebug($"waypoint reached cell=({completedWaypoint.X},{completedWaypoint.Y}) remaining={_plannedPath.Count}");

        if (_plannedPath.Count == 0) FinishCurrentRoute();
    }

    private void FinishCurrentRoute()
    {
        if (!IsMovementAuthority) return;
        if (PendingEnterContainerId is not null)
        {
            CurrentCommand = null;
            MovementStatus = MovementStatus.Idle;
            NavigationChanged();
            return;
        }
        if (TargetBuildingId is not null)
        {
            UpdateConstructionMovement();
            if (!IsBuilding && CurrentCommand is not null) OnPathSearchFailed();
            return;
        }
        StartNextQueuedOrder();
    }

    private void StartNextQueuedOrder()
    {
        // An empty queued route means no travel; skip it without recursion or
        // discarding the commands behind it.
        while (_commandQueue is { Count: > 0 })
        {
            var queued = _commandQueue.Dequeue();
            if (queued.Route is { Length: 0 }) continue;
            StartGoto(Globals.World, queued.Command, queued.Route);
            return;
        }
        ClearCommand();
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
        grid.TryUpdateFootprint(this);
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
        NavigationChanged();
    }

    public virtual bool TryReceiveGotoCommand(
        GameWorld map,
        GotoCommand command,
        bool appendToQueue = false,
        IReadOnlyList<Point>? route = null)
    {
        // The host uses an empty route both when the unit already occupies its
        // destination and when no useful route exists. In either case there
        // is nothing to execute; keeping CurrentCommand set would leave the
        // unit permanently "moving" without any waypoint to complete.
        if (!appendToQueue && route is { Count: 0 })
        {
            ClearCommand();
            return true;
        }
        if (appendToQueue && (CurrentCommand is not null || _plannedPath.Count > 0 || IsBuilding))
        {
            _commandQueue.Enqueue((command, route?.ToArray()));
            NavigationChanged();
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
        _pathRequestId++;
        _movementRetryCount = 0;
        _retryMovementSeconds = 0;
        MovementStatus = MovementStatus.Planning;
        NavigationChanged();
        _plannedPath.Clear();
        PathDebug($"path search requested target=({command.Target.X:0.0},{command.Target.Y:0.0}) request={_pathRequestId}");

        if (route is not null)
        {
            SetPlannedPath(route);
            return true;
        }
        if (!IsMovementAuthority) return true;
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

    internal void ClearPendingEnterContainer()
    {
        PendingEnterContainerId = null;
        NavigationChanged();
    }

    public virtual bool TryReceiveBuildConstructionCommand(
        GameWorld map,
        Building constructionSite, bool preserveQueue = false)
    {
        if (!preserveQueue)
        {
            _commandQueue.Clear();
            _plannedPath.Clear();
            _pathRequestId++;
            _movementRetryCount = 0;
            PendingEnterContainerId = null;
            CurrentCommand = null;
            IsBuilding = false;
        }
        TargetBuildingId = constructionSite.UnitId;
        NavigationChanged();
        if (!IsMovementAuthority) return true;
        if (map.GameGrid.AreFootprintsAdjacent(this, constructionSite))
        {
            BeginConstructionAtCurrentPosition();
            return true;
        }

        IReadOnlyList<Point> siteCells = map.GameGrid.GetFootprintCells(
            constructionSite, constructionSite.Position, GetYawDegrees(constructionSite.Transform));
        int margin = Math.Max(Width, Length) + 2;
        int minX = siteCells.Min(cell => cell.X) - margin;
        int maxX = siteCells.Max(cell => cell.X) + margin;
        int minY = siteCells.Min(cell => cell.Y) - margin;
        int maxY = siteCells.Max(cell => cell.Y) + margin;
        Point start = map.GameGrid.ToCell(Position);
        float yaw = GetYawDegrees(Transform);

        IEnumerable<Point> candidates =
            from y in Enumerable.Range(minY, maxY - minY + 1)
            from x in Enumerable.Range(minX, maxX - minX + 1)
            let cell = new Point(x, y)
            let world = map.GameGrid.ToWorldPosition(cell, Position.Y)
            where map.GameGrid.Contains(cell)
                && map.GameGrid.CanPlace(this, cell)
                && map.GameGrid.AreFootprintsAdjacent(
                    this, world, yaw,
                    constructionSite, constructionSite.Position, GetYawDegrees(constructionSite.Transform))
            orderby Vector3.DistanceSquared(Position, world)
            select cell;

        Point[] approaches = candidates.ToArray();
        int offset = approaches.Length == 0 ? 0 : (_movementRetryCount * 4) % approaches.Length;
        foreach (Point candidate in approaches.Skip(offset).Concat(approaches.Take(offset)).Take(4))
        {
            Vector3 world = map.GameGrid.ToWorldPosition(candidate, Position.Y);
            Vector2 target = new(world.X, world.Z);
            if (!map.PathfindingManager.TryFindPath(this, start, target, out List<Point> route))
                continue;

            bool accepted = StartGoto(map, new GotoCommand(target), route);
            if (accepted)
            {
                TargetBuildingId = constructionSite.UnitId;
                NavigationChanged();
                return true;
            }
        }

        CurrentCommand ??= new GotoCommand(new Vector2(constructionSite.Position.X, constructionSite.Position.Z));
        OnPathSearchFailed();
        return true;
    }

    public void SetPlannedPath(IReadOnlyList<Point> path)
    {
        ResetSteeringTarget();
        _plannedPath.Clear();
        _plannedPath.AddRange(path);
        _previousRouteCell = Globals.World?.GameGrid.ToCell(Position);
        _pathProgress = 0;
        _retryMovementSeconds = 0;
        MovementStatus = path.Count > 0 ? MovementStatus.FollowingRoute : MovementStatus.Idle;
        NavigationChanged();
        ResetMovementProgressWatchdog();
        PathDebug($"path applied waypoints={_plannedPath.Count}");
        if (path.Count == 0 && CurrentCommand is not null) FinishCurrentRoute();
    }

    public override void ClearCommand()
    {
        ResetSteeringTarget();
        _pathRequestId++;
        _retryMovementSeconds = 0;
        _movementRetryCount = 0;
        MovementStatus = MovementStatus.Idle;
        NavigationChanged();
        _blockedMovementSeconds = 0.0f;
        ResetMovementProgressWatchdog();
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

        float seconds = Math.Max(0, (float)gameTime.ElapsedGameTime.TotalSeconds);
        float correctionDecay = MathF.Exp(-seconds * 15.0f);
        _renderCorrectionOffset *= correctionDecay;
        _renderCorrectionYaw *= correctionDecay;
        _turningTowardPath = false;
        if (IsMovementAuthority)
        {
            UpdateFollowMovement(gameTime);
            UpdateAttackMovement(gameTime);
            UpdateConstructionMovement();
            UpdateMovementRecovery(seconds);
        }
        MoveAlongPath(gameTime);
        if (IsMovementAuthority) UpdateMovementProgressWatchdog(gameTime);
        AlignToTerrain(gameTime);
        base.Update(gameTime);
    }

    public override UnitState GetState() => GetMovementState(includeNavigation: true);

    internal UnitState GetMovementState(bool includeNavigation)
    {
        float yaw = GetYawDegrees(Transform);
        bool isMoving = CurrentCommand is not null || _plannedPath.Count > 0 || IsLeavingBuilding;
        GroundNavigationState? navigation = includeNavigation ? new(
            CurrentCommand?.Target.X, CurrentCommand?.Target.Y, _plannedPath.ToArray(),
            _commandQueue.Select(order => new QueuedMovementState(order.Command.Target.X,
                order.Command.Target.Y, order.Route)).ToArray(),
            TargetBuildingId, PendingEnterContainerId, IsBuilding, MovementStatus, _previousRouteCell) : null;
        return new UnitState(UnitId, StateRevision, StateTypeId, StateVersion,
            JsonSerializer.SerializeToUtf8Bytes(new MobileUnitState(
                Position.X, Position.Y, Position.Z, yaw, isMoving, SquadLeaderId,
                navigation, _pathProgress, _navigationRevision), NetworkJson.Options));
    }

    public override void ApplyState(UnitState state)
    {
        if (state.UnitId != UnitId || state.TypeId != StateTypeId ||
            state.Version != StateVersion || state.Revision < StateRevision)
            return;
        MobileUnitState? data = JsonSerializer.Deserialize<MobileUnitState>(state.Payload, NetworkJson.Options);
        if (data is null || !float.IsFinite(data.X) || !float.IsFinite(data.Y) ||
            !float.IsFinite(data.Z) || !float.IsFinite(data.YawDegrees))
            return;

        Matrix oldVisual = GetVisualWorldMatrix();
        Matrix transform = Matrix.CreateRotationY(MathHelper.ToRadians(data.YawDegrees));
        transform.Translation = new Vector3(data.X, data.Y, data.Z);
        bool positionApplied = Globals.World.GameGrid.TryApplyAuthoritativeTransform(this, transform);
        Vector3 correction = positionApplied ? oldVisual.Translation - Position : _renderCorrectionOffset;
        // Blend small prediction errors visually. Teleports must not sweep across the map.
        _renderCorrectionOffset = correction.LengthSquared() <= 16.0f ? correction : Vector3.Zero;
        if (positionApplied)
            _renderCorrectionYaw = correction.LengthSquared() <= 16.0f
                ? MathHelper.WrapAngle(MathHelper.ToRadians(GetYawDegrees(oldVisual) - data.YawDegrees)) : 0;
        if (data.Navigation is GroundNavigationState navigation)
        {
            CurrentCommand = navigation.TargetX is float x && navigation.TargetZ is float z &&
                float.IsFinite(x) && float.IsFinite(z) ? new GotoCommand(new Vector2(x, z)) : null;
            _commandQueue.Clear();
            foreach (QueuedMovementState queued in navigation.Queue)
                _commandQueue.Enqueue((new GotoCommand(new Vector2(queued.X, queued.Z)), queued.Route));
            TargetBuildingId = navigation.ConstructionSiteId;
            PendingEnterContainerId = navigation.ContainerId;
            IsBuilding = navigation.IsBuilding;
            MovementStatus = navigation.Status;
            _replicatedRoute = navigation.Route;
            _replicatedPreviousCell = navigation.PreviousCell;
            _replicatedRouteStartProgress = data.PathProgress;
            _replicatedNavigationRevision = data.NavigationRevision;
        }
        if (data.NavigationRevision == _replicatedNavigationRevision)
        {
            ResetSteeringTarget();
            int consumed = Math.Clamp(data.PathProgress - _replicatedRouteStartProgress, 0, _replicatedRoute.Length);
            _plannedPath.Clear();
            _plannedPath.AddRange(_replicatedRoute.Skip(consumed));
            _previousRouteCell = consumed > 0 ? _replicatedRoute[consumed - 1]
                : _replicatedPreviousCell ?? Globals.World.GameGrid.ToCell(Position);
            _pathProgress = data.PathProgress;
        }
        else if (data.Navigation is null && !data.IsMoving)
            ClearCommand(); // Compatibility with a position-only sender.
        SquadLeaderId = data.SquadLeaderId;
        StateRevision = state.Revision;
    }

    protected override Matrix GetVisualWorldMatrix()
    {
        Matrix visual = Matrix.CreateRotationY(_renderCorrectionYaw) * base.GetVisualWorldMatrix();
        visual.Translation += _renderCorrectionOffset;
        return visual;
    }

    private void UpdateConstructionMovement()
    {
        if (TargetBuildingId is not Guid buildingId)
            return;

        if (Globals.World.Units.FindById(buildingId) is not Building constructionSite ||
            constructionSite.IsDying || constructionSite.IsCompleted)
        {
            TargetBuildingId = null;
            IsBuilding = false;
            _plannedPath.Clear();
            StartNextQueuedOrder();
            return;
        }
        if (IsBuilding) return;

        if (Globals.World.GameGrid.AreFootprintsAdjacent(this, constructionSite))
            BeginConstructionAtCurrentPosition();
    }

    private void BeginConstructionAtCurrentPosition()
    {
        _pathRequestId++;
        _plannedPath.Clear();
        CurrentCommand = null;
        IsBuilding = true;
        MovementStatus = MovementStatus.Idle;
        NavigationChanged();
        PathDebug("construction footprints are adjacent; building starts");
    }

    private void UpdateMovementProgressWatchdog(GameTime gameTime)
    {
        if (CurrentCommand is null || _plannedPath.Count == 0 || IsBuilding || IsLeavingBuilding || _turningTowardPath)
        {
            ResetMovementProgressWatchdog();
            return;
        }

        Point waypoint = _steeringWaypoint is Point selected && _plannedPath.Contains(selected)
            ? selected : _plannedPath[0];
        Vector3 target = Globals.World.GameGrid.ToWorldPosition(waypoint, Position.Y);
        float distanceSquared = HorizontalDistanceSquared(Position, target);
        if (_progressWaypoint != waypoint ||
            MathF.Sqrt(distanceSquared) < MathF.Sqrt(_bestWaypointDistanceSquared) - MinimumProgressDistance)
        {
            _progressWaypoint = waypoint;
            _bestWaypointDistanceSquared = distanceSquared;
            _withoutPathProgressSeconds = 0.0f;
            return;
        }

        _withoutPathProgressSeconds += Math.Max(0.0f,
            (float)gameTime.ElapsedGameTime.TotalSeconds);
        if (_withoutPathProgressSeconds < MovementStallTimeoutSeconds)
            return;

        PathDebug($"movement watchdog requests a replacement route at cell=({waypoint.X},{waypoint.Y})");
        RequestMovementRecovery();
    }

    private void ResetMovementProgressWatchdog()
    {
        _progressWaypoint = null;
        _bestWaypointDistanceSquared = float.PositiveInfinity;
        _withoutPathProgressSeconds = 0.0f;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X;
        float z = first.Z - second.Z;
        return x * x + z * z;
    }

    private static float GetYawDegrees(Matrix transform)
    {
        Vector3 forward = transform.Forward;
        return MathHelper.ToDegrees(MathF.Atan2(-forward.X, -forward.Z));
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

    private Matrix CreateTerrainTransform(Terrain terrain)
    {
        float halfWidth = Math.Max(1, Width) * 0.5f;
        float halfLength = Math.Max(1, Length) * 0.5f;

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

            if (!AlignBodyToTerrain)
            {
                Vector3 uprightRight = horizontalRight;
                Vector3 uprightBack = -horizontalForward;
                float surfaceHeight = terrain.GetSurfaceHeight(position.X, position.Z);
                return new Matrix(
                    uprightRight.X, uprightRight.Y, uprightRight.Z, 0.0f,
                    0.0f, 1.0f, 0.0f, 0.0f,
                    uprightBack.X, uprightBack.Y, uprightBack.Z, 0.0f,
                    position.X, surfaceHeight, position.Z, 1.0f);
            }

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
