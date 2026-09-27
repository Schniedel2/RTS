using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace RTS.Network;

public sealed class NetworkHost
{
    private readonly NetworkHandler _networkHandler;
    private readonly GameWorld _world;
    private readonly EarthworkController _earthworks;
    private readonly Queue<NetworkMessage> _earthworkBroadcasts = new();
    private readonly ConcurrentQueue<NetworkMessage> _requestQueue = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly Dictionary<Guid, Vector2> _gotoQueueEnds = [];
    private readonly Dictionary<Guid, HarvestJob> _harvestJobs = [];
    private readonly Dictionary<Guid, int> _startPositionWishes = [];
    private readonly Dictionary<Guid, double> _nextPatientHealTimes = [];
    private readonly Dictionary<Guid, MedicJob> _medicJobs = [];
    private readonly HashSet<Guid> _medicsHoldingPosition = [];
    private const int MaximumQueuedRequests = 1024;
    private const int MaximumRequestsPerUpdate = 32;
    private const int MaximumStateUpdatesPerTick = 32;
    private const double HostSimulationInterval = 0.1;
    private const double StateHeartbeatInterval = 3.0;
    private double _hostTime;
    private double _simulationAccumulator;
    /// <summary>Authoritative simulation time, shared with clients via NetworkHandler.EstimatedHostTime.</summary>
    public double HostTime => _hostTime;
    private readonly List<HostProjectile> _hostProjectiles = [];
    private readonly List<ProjectileImpact> _projectileImpacts = [];
    private sealed class HarvestJob(Point fieldCenter)
    {
        public Point FieldCenter { get; } = fieldCenter;
        public HarvestPhase Phase { get; set; } = HarvestPhase.DrivingToField;
        public Point? ResourceCell { get; set; }
        public Guid? SiloId { get; set; }
        public bool MoveIssued { get; set; }
        public float UnloadElapsed { get; set; }
        public bool ResumeAfterUnload { get; set; } = true;
        public Vector3? StorageApproach { get; set; }
    }
    private sealed class MedicJob(Guid patientId)
    {
        public Guid PatientId { get; } = patientId;
        public bool MoveIssued { get; set; }
    }
    private const int HarvestSearchRadius = 24;
    private const float HarvestRatePerSecond = 18.0f;
    private const float UnloadSeconds = 2.0f;

    private sealed class HostProjectile(
        Guid projectileId,
        Guid attackerId,
        Vector3 start,
        Vector3 initialVelocity,
        ProjectileKind kind,
        float damage,
        DamageType damageType)
    {
        public Guid ProjectileId { get; } = projectileId;
        public Guid AttackerId { get; } = attackerId;
        public Vector3 Start { get; } = start;
        public Vector3 InitialVelocity { get; } = initialVelocity;
        public ProjectileKind Kind { get; } = kind;
        public float Damage { get; } = damage;
        public DamageType DamageType { get; } = damageType;
        public ProjectileFlightProfile Profile { get; } = ProjectileFlightProfile.For(kind);
        public float Age { get; set; }
    }

    private sealed record ProjectileImpact(
        Guid ProjectileId,
        Guid AttackerId,
        Vector3 Position,
        Vector3 Normal,
        Guid? HitUnitId,
        float Damage,
        DamageType DamageType,
        float ExplosionRadius);

    public NetworkHost(
        NetworkHandler networkHandler,
        NetworkInput networkInput,
        GameWorld world)
    {
        _networkHandler = networkHandler;
        _world = world;
        _earthworks = new EarthworkController(world, networkHandler.LocalPeerId, command =>
        {
            networkHandler.EnqueueLocalMessage(command);
            _earthworkBroadcasts.Enqueue(command);
        });
        networkInput.MessageReceived += HandleMessage;
    }

    private async Task PublishEarthworkAsync()
    {
        // Keep patches, completion and replacement commands in order for every peer.
        while (_earthworkBroadcasts.TryDequeue(out NetworkMessage? command))
            await _networkHandler.BroadcastAsync(command, CancellationToken.None);
    }

    private async Task PublishHelicoptersAsync()
    {
        foreach (Helicopter helicopter in _world.Units.Units.OfType<Helicopter>().ToArray())
        {
            if (helicopter.HitPoints <= 0)
            {
                _world.Units.Destroy(helicopter.UnitId);
                var destroy = NetworkCommands.CreateDestroyUnitCommand(_networkHandler.LocalPeerId, helicopter.UnitId);
                _networkHandler.EnqueueLocalMessage(destroy);
                await _networkHandler.BroadcastAsync(destroy);
            }
            else if (_hostTime >= helicopter.NextNetworkUpdateTime)
            {
                await _networkHandler.BroadcastAsync(NetworkCommands.CreateUnitStateCommand(_networkHandler.LocalPeerId, helicopter.GetState()));
                helicopter.NextNetworkUpdateTime = _hostTime + HostSimulationInterval;
            }
        }
    }

    private async Task PublishGroundMobileUnitsAsync()
    {
        int sentUpdates = 0;
        foreach (MobileUnit unit in _world.Units.Units.OfType<MobileUnit>().ToArray())
        {
            if (sentUpdates >= MaximumStateUpdatesPerTick)
                break;
            if (unit is Helicopter || unit.IsEmbarked || unit.IsDying || unit.IsLeavingBuilding ||
                _hostTime < unit.NextNetworkUpdateTime)
                continue;

            await _networkHandler.BroadcastAsync(
                NetworkCommands.CreateUnitStateCommand(_networkHandler.LocalPeerId, unit.GetState()));
            unit.NextNetworkUpdateTime = _hostTime + HostSimulationInterval;
            sentUpdates++;
        }
    }

    private NetworkMessage? TryCreateHelicopterOrder(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindById(id) is not Helicopter helicopter ||
            !Globals.Game.Armies.CanControl(request.SenderId, helicopter.ArmyId) ||
            request.HelicopterOrder is not HelicopterOrder order || !Enum.IsDefined(order) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Z)) return null;
        bool accepted;
        if (order == HelicopterOrder.TakeOff) { helicopter.Stop(); accepted = helicopter.TakeOff(_world); }
        else if (order == HelicopterOrder.ReturnToHelipad) accepted = helicopter.ReturnToHelipad(_world);
        else accepted = helicopter.RequestLanding(_world, new(request.X, request.Z),
            request.TargetId is Guid padId ? _world.Units.FindById(padId) as Helipad : null);
        if (!accepted) return null;
        helicopter.StateRevision++;
        return NetworkCommands.CreateUnitStateCommand(_networkHandler.LocalPeerId, helicopter.GetState());
    }

    private NetworkMessage? TryCreateAttackCommand(NetworkMessage request)
    {
        List<Guid> accepted = [];
        foreach (Guid id in (request.UnitIds ?? Array.Empty<Guid>()).Distinct())
        {
            if (_world.Units.FindById(id) is not Unit attacker) continue;
            if (attacker is Helicopter helicopter)
            {
                if (request.SenderId != _networkHandler.LocalPeerId && !Globals.Game.Armies.CanControl(request.SenderId, helicopter.ArmyId)) continue;
                Vector2 offset = new(request.X - helicopter.Position.X, request.Z - helicopter.Position.Z);
                if (!float.IsFinite(request.Y) || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y) || offset.LengthSquared() > helicopter.AttackRange * helicopter.AttackRange ||
                    !helicopter.TryAuthorizeShot(_hostTime)) continue;
            }
            accepted.Add(id);
        }
        return accepted.Count == 0 ? null : NetworkCommands.CreateAttackCommand(_networkHandler.LocalPeerId, request with { UnitIds = accepted.ToArray() });
    }

    private NetworkMessage? TryCreateAttackTargetCommand(NetworkMessage request)
    {
        if (request.TargetId is not Guid targetId ||
            _world.Units.FindById(targetId) is not Unit target)
            return null;

        Guid[] accepted = ExpandSquadUnitIds(request.UnitIds ?? Array.Empty<Guid>())
            .Distinct()
            .Where(id => _world.Units.FindById(id) is Unit attacker &&
                (request.SenderId == _networkHandler.LocalPeerId ||
                 Globals.Game.Armies.CanControl(request.SenderId, attacker.ArmyId)) &&
                attacker.CanAttackTarget(target))
            .ToArray();
        return accepted.Length == 0
            ? null
            : NetworkCommands.CreateAttackTargetCommand(
                _networkHandler.LocalPeerId, request with { UnitIds = accepted });
    }

    private NetworkMessage? TryCreateAttackGroundCommand(NetworkMessage request)
    {
        Guid[] accepted = ExpandSquadUnitIds(request.UnitIds ?? Array.Empty<Guid>())
            .Distinct()
            .Where(id => _world.Units.FindById(id) is Unit attacker &&
                (request.SenderId == _networkHandler.LocalPeerId ||
                 Globals.Game.Armies.CanControl(request.SenderId, attacker.ArmyId)) &&
                attacker.CanAttackDomain(TargetDomain.Ground))
            .ToArray();
        return accepted.Length == 0
            ? null
            : NetworkCommands.CreateAttackGroundCommand(
                _networkHandler.LocalPeerId, request with { UnitIds = accepted });
    }

    private void HandleMessage(NetworkMessage message)
    {
        if (!_networkHandler.IsHost)
            return;

        if (message.Type != NetworkMessageType.SpawnRequest &&
            message.Type != NetworkMessageType.GotoRequest &&
            message.Type != NetworkMessageType.StopRequest &&
            message.Type != NetworkMessageType.AttackRequest &&
            message.Type != NetworkMessageType.AttackTargetRequest &&
            message.Type != NetworkMessageType.AttackGroundRequest &&
            message.Type != NetworkMessageType.FollowRequest &&
            message.Type != NetworkMessageType.ToolActionRequest &&
            message.Type != NetworkMessageType.UnitActionRequest &&
            message.Type != NetworkMessageType.BuildRequest &&
            message.Type != NetworkMessageType.BuildConstructionRequest &&
            message.Type != NetworkMessageType.TrainUnitRequest &&
            message.Type != NetworkMessageType.ResearchRequest &&
            message.Type != NetworkMessageType.SetRallyPointRequest &&
            message.Type != NetworkMessageType.EarthworkRequest &&
            message.Type != NetworkMessageType.HelicopterOrderRequest &&
            message.Type != NetworkMessageType.HarvestRequest &&
            message.Type != NetworkMessageType.MoveAwayRequest &&
            message.Type != NetworkMessageType.HarvesterReturnRequest &&
            message.Type != NetworkMessageType.CancelConstructionRequest &&
            message.Type != NetworkMessageType.SellBuildingRequest &&
            message.Type != NetworkMessageType.DestroyBuildingRequest &&
            message.Type != NetworkMessageType.StartPositionWishRequest &&
            message.Type != NetworkMessageType.StartMultiplayerGameRequest &&
            message.Type != NetworkMessageType.EnterUnitRequest &&
            message.Type != NetworkMessageType.LeaveContainerRequest &&
            message.Type != NetworkMessageType.NotifyUnitsSelected &&
            message.Type != NetworkMessageType.GrantArmyControlRequest &&
            message.Type != NetworkMessageType.RevokeArmyControlRequest &&
            message.Type != NetworkMessageType.TransferUnitRequest &&
            message.Type != NetworkMessageType.MergeArmiesRequest &&
            message.Type != NetworkMessageType.TextRequest &&
            message.Type != NetworkMessageType.RequestPlayerUpdate)
            return;
        if (message.Type == NetworkMessageType.SpawnRequest && message.UnitTypeId is null)
            return;

        if (message.Type == NetworkMessageType.TextRequest && string.IsNullOrWhiteSpace(message.Text))
            return;

        if (message.Type == NetworkMessageType.RequestPlayerUpdate &&
            (message.PlayerId is null || string.IsNullOrWhiteSpace(message.DisplayName)))
            return;

        if (message.Type == NetworkMessageType.BuildConstructionRequest &&
            (message.ConstructionSiteId is null || message.UnitIds is null || message.UnitIds.Length == 0))
            return;

        if (message.Type == NetworkMessageType.TrainUnitRequest &&
            (message.UnitId is null || string.IsNullOrWhiteSpace(message.UnitTypeId)))
            return;
        if (message.Type == NetworkMessageType.ResearchRequest &&
            (message.UnitId is null || string.IsNullOrWhiteSpace(message.UnitTypeId)))
            return;

        if (message.Type == NetworkMessageType.EnterUnitRequest &&
            (message.UnitId is null || message.TargetId is null))
            return;

        if (message.Type == NetworkMessageType.LeaveContainerRequest && message.UnitId is null)
            return;

        if (_requestQueue.Count >= MaximumQueuedRequests)
            return;

        _requestQueue.Enqueue(message);
    }

    public async Task UpdateAsync(GameTime gameTime)
    {
        if (!await _updateGate.WaitAsync(0))
            return;

        try
        {
            UpdateHostSimulation(gameTime);
            await PublishEarthworkAsync();
            await PublishHelicoptersAsync();
            await PublishGroundMobileUnitsAsync();
            await PublishHarvestersAsync(gameTime);
            await PublishProjectileImpactsAsync();
            await UpdateAndPublishMedicsAsync();

            for (int index = 0; index < MaximumRequestsPerUpdate; index++)
            {
                if (!_requestQueue.TryDequeue(out NetworkMessage? request))
                    return;

                if (request.Type is NetworkMessageType.GotoRequest or NetworkMessageType.StopRequest or
                    NetworkMessageType.FollowRequest or NetworkMessageType.AttackTargetRequest or NetworkMessageType.AttackGroundRequest)
                    request = request with { UnitIds = (request.UnitIds ?? Array.Empty<Guid>()).Where(id =>
                        _world.Units.FindById(id) is not Helicopter helicopter || Globals.Game.Armies.CanControl(request.SenderId, helicopter.ArmyId)).ToArray() };
                HandleMedicCommandOverride(request);
                _earthworks.CancelForRequest(request);
                if (request.Type is NetworkMessageType.GotoRequest or NetworkMessageType.MoveAwayRequest && request.UnitId is Guid singleId)
                    _harvestJobs.Remove(singleId);
                if (request.Type == NetworkMessageType.GotoRequest)
                    foreach (Guid id in request.UnitIds ?? []) _harvestJobs.Remove(id);
                NetworkMessage? command = request.Type switch
                {
                    NetworkMessageType.SpawnRequest => NetworkCommands.CreateSpawnCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.GotoRequest => TryCreateGotoCommand(request),
                    NetworkMessageType.StopRequest => CreateStopCommand(request),
                    NetworkMessageType.AttackRequest => TryCreateAttackCommand(request),
                    NetworkMessageType.AttackTargetRequest => TryCreateAttackTargetCommand(request),
                    NetworkMessageType.AttackGroundRequest => TryCreateAttackGroundCommand(request),
                    NetworkMessageType.FollowRequest => NetworkCommands.CreateFollowCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.TextRequest => NetworkCommands.CreateTextCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.ToolActionRequest => NetworkCommands.CreateToolActionCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.UnitActionRequest => TryCreateUnitActionCommand(request),
                    NetworkMessageType.RequestPlayerUpdate => NetworkCommands.CreatePlayerUpdateCommand(
                        _networkHandler.LocalPeerId,
                        request,
                        ConfirmPlayerSkin(request),
                        ConfirmTeamId(request)),
                    NetworkMessageType.BuildRequest => TryCreateBuildCommand(request),
                    NetworkMessageType.BuildConstructionRequest => NetworkCommands.CreateBuildConstructionCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.TrainUnitRequest => TryCreateTrainUnitCommand(request),
                    NetworkMessageType.ResearchRequest => TryCreateResearchCommand(request),
                    NetworkMessageType.SetRallyPointRequest => TryCreateSetRallyPointCommand(request),
                    NetworkMessageType.EarthworkRequest => _earthworks.Start(request),
                    NetworkMessageType.HelicopterOrderRequest => TryCreateHelicopterOrder(request),
                    NetworkMessageType.HarvestRequest => TryCreateHarvestCommand(request),
                    NetworkMessageType.MoveAwayRequest => TryCreateMoveAwayCommand(request),
                    NetworkMessageType.HarvesterReturnRequest => TryCreateHarvesterReturnCommand(request),
                    NetworkMessageType.CancelConstructionRequest => TryCreateCancelConstructionCommand(request),
                    NetworkMessageType.SellBuildingRequest => TryCreateSellBuildingCommand(request),
                    NetworkMessageType.DestroyBuildingRequest => TryCreateDestroyBuildingCommand(request),
                    NetworkMessageType.StartPositionWishRequest => RememberStartPositionWish(request),
                    NetworkMessageType.StartMultiplayerGameRequest => TryCreateStartMultiplayerGameCommand(request),
                    NetworkMessageType.EnterUnitRequest => TryCreateEnterUnitCommand(request),
                    NetworkMessageType.LeaveContainerRequest => TryCreateLeaveContainerCommand(request),
                    NetworkMessageType.NotifyUnitsSelected => request,
                    NetworkMessageType.GrantArmyControlRequest => NetworkCommands.CreateArmyControlCommand(_networkHandler.LocalPeerId, request, grant: true),
                    NetworkMessageType.RevokeArmyControlRequest => NetworkCommands.CreateArmyControlCommand(_networkHandler.LocalPeerId, request, grant: false),
                    NetworkMessageType.TransferUnitRequest => CreateTransferUnitCommand(request),
                    NetworkMessageType.MergeArmiesRequest => NetworkCommands.CreateMergeArmiesCommand(_networkHandler.LocalPeerId, request),
                    _ => throw new InvalidOperationException($"Unsupported request type: {request.Type}")
                };

                await PublishEarthworkAsync();
                if (command is null)
                    continue;

                _networkHandler.EnqueueLocalMessage(command);
                await _networkHandler.BroadcastAsync(command, CancellationToken.None);

                if (request.Type == NetworkMessageType.AttackRequest)
                    await ResolveGroundAttackAsync(request with { UnitIds = command.UnitIds });
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private NetworkMessage CreateTransferUnitCommand(NetworkMessage request)
    {
        Player? recipient = request.TargetId is Guid playerId
            ? Globals.Game.Players.FirstOrDefault(player => player.Id == playerId)
            : null;
        return NetworkCommands.CreateTransferUnitCommand(
            _networkHandler.LocalPeerId, request, recipient?.ArmyId ?? Guid.Empty);
    }

    private NetworkMessage? TryCreateGotoCommand(NetworkMessage request)
    {
        Vector2 target = new(request.X, request.Z);
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) return null;
        Point destination = _world.GameGrid.ToCell(new Vector3(target.X, 0, target.Y));
        if (!_world.GameGrid.Contains(destination)) return null;
        List<UnitRoute> routes = [];
        Guid[] requestedIds = request.UnitIds ?? [];
        Dictionary<Guid, Vector2> formationTargets = [];
        foreach (SquadLeader leader in requestedIds.Distinct()
            .Select(_world.Units.FindById)
            .OfType<SquadLeader>()
            .Where(leader => Globals.Game.Armies.CanControl(request.SenderId, leader.ArmyId)))
        {
            Soldier[] members = GetSquadMembers(leader).ToArray();
            float? facingDegrees = request.FormationFacingDegrees;
            if (facingDegrees is null && request.AppendToQueue &&
                _gotoQueueEnds.TryGetValue(leader.UnitId, out Vector2 queuedLeaderEnd))
            {
                Vector2 direction = target - queuedLeaderEnd;
                if (direction.LengthSquared() > 0.01f)
                    facingDegrees = MathHelper.ToDegrees(
                        MathF.Atan2(-direction.X, -direction.Y));
            }
            foreach ((Guid unitId, Vector2 position) in SquadFormation.CreateAssignments(
                leader, members, target, facingDegrees, _world.GameGrid.CellSize))
                formationTargets[unitId] = position;
        }

        MobileUnit[] units = ExpandSquadUnitIds(requestedIds)
            .Distinct()
            .Select(_world.Units.FindMobileUnitById)
            .Where(unit => unit is not null && Globals.Game.Armies.CanControl(request.SenderId, unit.ArmyId))
            .Cast<MobileUnit>()
            .OrderBy(unit => Vector2.DistanceSquared(new Vector2(unit.Position.X, unit.Position.Z), target))
            .ToArray();
        HashSet<Point> reservedDestinations = [];
        bool distributeGroup = units.Length > 1;

        foreach (MobileUnit unit in units)
        {
            Guid id = unit.UnitId;
            Vector2 requestedTarget = formationTargets.GetValueOrDefault(id, target);
            Point requestedDestination = _world.GameGrid.ToCell(
                new Vector3(requestedTarget.X, 0.0f, requestedTarget.Y));
            if (!_world.GameGrid.Contains(requestedDestination))
                requestedDestination = destination;

            // Helicopters fly directly to their world-space destination.
            // Ground placement, occupied cells, slopes and terrain routes do
            // not constrain an airborne Goto command. The Helicopter validates
            // map bounds and fuel when the replicated command is applied.
            if (unit is Helicopter)
            {
                routes.Add(new UnitRoute(id, [], requestedTarget.X, requestedTarget.Y));
                _gotoQueueEnds[id] = requestedTarget;
                continue;
            }

            Point[]? proposed = !distributeGroup && formationTargets.Count == 0
                ? request.Routes?.FirstOrDefault(route => route.UnitId == id)?.Cells
                : null;
            bool plausible = proposed is { Length: <= 4096 } && proposed.All(_world.GameGrid.Contains) &&
                (proposed.Length == 0 || proposed[^1] == requestedDestination);
            Vector2 assignedTarget = requestedTarget;
            if (plausible)
            {
                reservedDestinations.Add(requestedDestination);
            }
            else if (!TryAssignGotoDestination(unit, requestedTarget, requestedDestination, reservedDestinations,
                request.AppendToQueue, distributeGroup, out assignedTarget, out proposed))
            {
                // No useful position is reachable near the group destination.
                // Sending an empty route cancels an older movement order and
                // leaves the unit standing instead of running against a blocker.
                assignedTarget = new Vector2(unit.Position.X, unit.Position.Z);
                proposed = [];
            }

            routes.Add(new UnitRoute(id, proposed!, assignedTarget.X, assignedTarget.Y));
            _gotoQueueEnds[id] = assignedTarget;
        }
        return routes.Count == 0 ? null : NetworkCommands.CreateGotoCommand(_networkHandler.LocalPeerId,
            request with { UnitIds = routes.Select(route => route.UnitId).ToArray() }, routes.ToArray());
    }

    private bool TryAssignGotoDestination(
        MobileUnit unit,
        Vector2 requestedTarget,
        Point center,
        HashSet<Point> reservedDestinations,
        bool appendToQueue,
        bool distributeGroup,
        out Vector2 assignedTarget,
        out Point[] route)
    {
        Vector2 startPosition = appendToQueue && _gotoQueueEnds.TryGetValue(unit.UnitId, out Vector2 queuedEnd)
            ? queuedEnd
            : new Vector2(unit.Position.X, unit.Position.Z);
        Point start = _world.GameGrid.ToCell(new Vector3(startPosition.X, 0, startPosition.Y));
        int maximumRadius = distributeGroup ? Math.Max(6, (int)MathF.Ceiling(MathF.Sqrt(reservedDestinations.Count + 1)) + 3) : 0;

        for (int radius = 0; radius <= maximumRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius)
                        continue;
                    Point candidate = center + new Point(x, y);
                    if (reservedDestinations.Contains(candidate) || !_world.GameGrid.Contains(candidate) ||
                        !unit.MovementProfile.CanEnter(_world, unit, candidate) ||
                        !_world.GameGrid.IsPathfindingAllowed(unit, candidate))
                        continue;

                    Vector3 candidatePosition = _world.GameGrid.ToWorldPosition(candidate, unit.Position.Y);
                    Vector2 candidateTarget = radius == 0
                        ? requestedTarget
                        : new Vector2(candidatePosition.X, candidatePosition.Z);
                    if (!_world.PathfindingManager.TryFindPath(unit, start, candidateTarget, out List<Point> path))
                        continue;

                    reservedDestinations.Add(candidate);
                    assignedTarget = candidateTarget;
                    route = path.ToArray();
                    return true;
                }
            }
        }

        assignedTarget = default;
        route = [];
        return false;
    }

    private NetworkMessage CreateStopCommand(NetworkMessage request)
    {
        Guid[] ids = ExpandSquadUnitIds(request.UnitIds ?? [])
            .Where(id => _world.Units.FindById(id) is Unit unit &&
                Globals.Game.Armies.CanControl(request.SenderId, unit.ArmyId))
            .ToArray();
        foreach (Guid id in ids)
        {
            _gotoQueueEnds.Remove(id);
            _harvestJobs.Remove(id);
        }
        return NetworkCommands.CreateStopCommand(
            _networkHandler.LocalPeerId, request with { UnitIds = ids });
    }

    private NetworkMessage? TryCreateUnitActionCommand(NetworkMessage request)
    {
        if (request.UnitActionType is not UnitActionType actionType ||
            actionType is UnitActionType.None or UnitActionType.max ||
            request.UnitIds is not { Length: > 0 } requestedIds ||
            request.UnitActionContext is not { } context ||
            context.TargetPosition is { IsFinite: false } ||
            (context.FloatValue is float floatValue && !float.IsFinite(floatValue)) ||
            context.Value?.Length > 256)
        {
            return null;
        }

        Guid[] acceptedIds = requestedIds.Distinct().Where(id =>
            _world.Units.FindById(id) is Unit unit && !unit.IsDying &&
            Globals.Game.Armies.CanControl(request.SenderId, unit.ArmyId)).ToArray();
        if (acceptedIds.Length == 0)
            return null;
        if (actionType == UnitActionType.Stop)
            return CreateStopCommand(request with { UnitIds = acceptedIds });
        if (actionType == UnitActionType.AssembleSquad)
        {
            SquadLeader? leader = acceptedIds.Select(_world.Units.FindById)
                .OfType<SquadLeader>().FirstOrDefault();
            if (leader is null || leader.ArmyId is not Guid armyId)
                return null;

            float radius = SquadFormation.AssembleRadiusInCells * _world.GameGrid.CellSize;
            Soldier[] members = _world.Units.Units.OfType<Soldier>()
                .Where(unit => unit != leader && unit is not SquadLeader && !unit.IsDying && !unit.IsEmbarked &&
                    unit.ArmyId == armyId &&
                    (unit.SquadLeaderId == leader.UnitId ||
                     unit.SquadLeaderId is null &&
                     Vector3.DistanceSquared(unit.Position, leader.Position) <= radius * radius))
                .OrderBy(unit => unit.SquadLeaderId == leader.UnitId ? 0 : 1)
                .ThenBy(unit => Vector3.DistanceSquared(unit.Position, leader.Position))
                .ThenBy(unit => unit.UnitId)
                .Take(SquadFormation.MaximumMembers)
                .ToArray();
            Guid[] squadIds = [leader.UnitId, .. members.Select(member => member.UnitId)];
            UnitActionContext squadContext = context with { TargetUnitId = leader.UnitId };
            return NetworkCommands.CreateUnitActionCommand(_networkHandler.LocalPeerId,
                request with { UnitActionContext = squadContext }, squadIds);
        }
        if (actionType == UnitActionType.DisbandSquad)
        {
            SquadLeader? leader = acceptedIds.Select(_world.Units.FindById)
                .OfType<SquadLeader>().FirstOrDefault();
            if (leader is null)
                return null;
            Guid[] squadIds = [leader.UnitId, .. GetSquadMembers(leader).Select(member => member.UnitId)];
            UnitActionContext squadContext = context with { TargetUnitId = leader.UnitId };
            return NetworkCommands.CreateUnitActionCommand(_networkHandler.LocalPeerId,
                request with { UnitActionContext = squadContext }, squadIds);
        }
        if (actionType is UnitActionType.AIStartReactor or UnitActionType.AIStartRefinery or
            UnitActionType.AIStartEconomy or UnitActionType.AIStartScouting or UnitActionType.AIStopGoals)
        {
            CommandCenter? commandCenter = acceptedIds
                .Select(_world.Units.FindById)
                .OfType<CommandCenter>()
                .FirstOrDefault();
            if (commandCenter is null || !Globals.Game.ApplyCommandCenterGoal(commandCenter, actionType))
                return null;
            acceptedIds = [commandCenter.UnitId];
        }
        return NetworkCommands.CreateUnitActionCommand(_networkHandler.LocalPeerId, request, acceptedIds);
    }

    private IEnumerable<Guid> ExpandSquadUnitIds(IEnumerable<Guid> requestedIds)
    {
        HashSet<Guid> result = requestedIds.ToHashSet();
        foreach (SquadLeader leader in result.Select(_world.Units.FindById).OfType<SquadLeader>().ToArray())
            foreach (Soldier member in GetSquadMembers(leader))
                result.Add(member.UnitId);
        return result;
    }

    private IEnumerable<Soldier> GetSquadMembers(SquadLeader leader) =>
        _world.Units.Units.OfType<Soldier>().Where(unit =>
            unit != leader && unit.SquadLeaderId == leader.UnitId && !unit.IsDying);

    private NetworkMessage? TryCreateHarvestCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindById(id) is not Harvester harvester ||
            harvester.IsDying || !Globals.Game.Armies.CanControl(request.SenderId, harvester.ArmyId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Z)) return null;
        Point center = _world.GameGrid.ToCell(new Vector3(request.X, 0, request.Z));
        if (!_world.GameGrid.Contains(center)) return null;
        harvester.Stop();
        harvester.ApplyHarvestState(HarvestPhase.DrivingToField, harvester.CargoAmount);
        _harvestJobs[id] = new HarvestJob(center);
        return CreateHarvestStateCommand(harvester, HarvestPhase.DrivingToField);
    }

    private NetworkMessage? TryCreateHarvesterReturnCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindById(id) is not Harvester harvester ||
            harvester.IsDying || harvester.CargoAmount <= 0.001f ||
            !Globals.Game.Armies.CanControl(request.SenderId, harvester.ArmyId) ||
            FindNearestSilo(harvester) is not Building storage)
            return null;

        harvester.Stop();
        harvester.ApplyHarvestState(HarvestPhase.ReturningToSilo, harvester.CargoAmount);
        _harvestJobs[id] = new HarvestJob(_world.GameGrid.ToCell(harvester.Position))
        {
            Phase = HarvestPhase.ReturningToSilo,
            SiloId = storage.UnitId,
            ResumeAfterUnload = false
        };
        return CreateHarvestStateCommand(harvester, HarvestPhase.ReturningToSilo);
    }

    private NetworkMessage? TryCreateMoveAwayCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindMobileUnitById(id) is not MobileUnit unit ||
            unit.IsDying || !Globals.Game.Armies.CanControl(request.SenderId, unit.ArmyId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Z)) return null;

        Vector2 away = new(unit.Position.X - request.X, unit.Position.Z - request.Z);
        if (away.LengthSquared() < 0.01f)
            away = new Vector2(unit.Transform.Forward.X, unit.Transform.Forward.Z);
        if (away.LengthSquared() < 0.01f)
            away = Vector2.UnitX;
        away.Normalize();

        float cellSize = _world.GameGrid.CellSize;
        float[] angleOffsets = [0, 22.5f, -22.5f, 45.0f, -45.0f, 67.5f, -67.5f, 90.0f, -90.0f];
        for (int distanceInCells = 6; distanceInCells >= 2; distanceInCells--)
        {
            foreach (float offset in angleOffsets)
            {
                Vector2 direction = Vector2.Transform(away, Matrix.CreateRotationZ(MathHelper.ToRadians(offset)));
                Vector2 destination = new Vector2(unit.Position.X, unit.Position.Z) + direction * distanceInCells * cellSize;
                NetworkMessage candidate = NetworkCommands.CreateGotoRequest(request.SenderId, [id],
                    destination.X, unit.Position.Y, destination.Y);
                NetworkMessage? command = TryCreateGotoCommand(candidate);
                if (command is not null) return command;
            }
        }
        return null;
    }

    private async Task PublishHarvestersAsync(GameTime gameTime)
    {
        float elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        foreach ((Guid id, HarvestJob job) in _harvestJobs.ToArray())
        {
            if (_world.Units.FindById(id) is not Harvester harvester || harvester.IsDying)
            {
                _harvestJobs.Remove(id);
                continue;
            }
            if (job.Phase == HarvestPhase.DrivingToField)
            {
                if (harvester.CargoAmount >= harvester.CargoCapacity - 0.001f || !TryFindTiberium(job.FieldCenter, out Point resourceCell))
                {
                    if (harvester.CargoAmount <= 0.001f) { await EndHarvestAsync(harvester); continue; }
                    await BeginReturnAsync(harvester, job); continue;
                }
                job.ResourceCell = resourceCell;
                Vector3 target = _world.GameGrid.ToWorldPosition(resourceCell, 0);
                if (!job.MoveIssued) { job.MoveIssued = await PublishHarvesterGotoAsync(harvester, target); continue; }
                if (harvester.CurrentCommand is null)
                {
                    if (HorizontalDistanceSquared(harvester.Position, target) <= 4.0f)
                    {
                        job.Phase = HarvestPhase.Harvesting; job.MoveIssued = false;
                        await PublishHarvestStateAsync(harvester, job.Phase);
                    }
                    else job.MoveIssued = false;
                }
                continue;
            }
            if (job.Phase == HarvestPhase.Harvesting)
            {
                if (job.ResourceCell is not Point cell || !_world.Tiberium.Cells.ContainsKey(cell))
                {
                    job.Phase = HarvestPhase.DrivingToField;
                    await PublishHarvestStateAsync(harvester, job.Phase); continue;
                }
                float harvested = _world.Tiberium.TryHarvest(cell,
                    Math.Min(harvester.CargoCapacity - harvester.CargoAmount, HarvestRatePerSecond * elapsed), _hostTime);
                if (harvested > 0)
                {
                    harvester.ApplyHarvestState(job.Phase, harvester.CargoAmount + harvested);
                    float remaining = _world.Tiberium.Cells.TryGetValue(cell, out TiberiumCell? value) ? value.Amount : 0;
                    await PublishAsync(new(NetworkMessageType.TiberiumHarvestCommand, _networkHandler.LocalPeerId,
                        CellX: cell.X, CellZ: cell.Y, TiberiumAmount: remaining, ServerTime: _hostTime));
                    await PublishHarvestStateAsync(harvester, job.Phase);
                }
                if (harvester.CargoAmount >= harvester.CargoCapacity - 0.001f) await BeginReturnAsync(harvester, job);
                else if (!_world.Tiberium.Cells.ContainsKey(cell))
                {
                    job.Phase = HarvestPhase.DrivingToField;
                    await PublishHarvestStateAsync(harvester, job.Phase);
                }
                continue;
            }
            if (job.Phase == HarvestPhase.ReturningToSilo)
            {
                Building? silo = job.SiloId is Guid siloId ? _world.Units.FindById(siloId) as Building : null;
                if (silo is null || silo.IsDying || !silo.IsCompleted || silo.ArmyId != harvester.ArmyId ||
                    silo.AvailableResourceCapacity <= 0.001f)
                {
                    silo = FindNearestSilo(harvester); job.SiloId = silo?.UnitId;
                    job.MoveIssued = false; job.StorageApproach = null;
                }
                if (silo is null) { await EndHarvestAsync(harvester); continue; }
                if (job.StorageApproach is not Vector3 unload)
                {
                    Vector3 preferredUnload = silo.GetResourceUnloadPosition();
                    if (!ProductionExitResolver.TryResolve(_world, silo, harvester,
                        harvester.Position, preferredUnload, out unload))
                    {
                        await EndHarvestAsync(harvester);
                        continue;
                    }
                    job.StorageApproach = unload;
                }
                if (!job.MoveIssued) { job.MoveIssued = await PublishHarvesterGotoAsync(harvester, unload); continue; }
                if (harvester.CurrentCommand is null)
                {
                    if (HorizontalDistanceSquared(harvester.Position, unload) <= 6.25f)
                    {
                        job.Phase = HarvestPhase.Unloading; job.UnloadElapsed = 0;
                        await PublishHarvestStateAsync(harvester, job.Phase);
                    }
                    else job.MoveIssued = false;
                }
                continue;
            }
            job.UnloadElapsed += elapsed;
            if (job.UnloadElapsed < UnloadSeconds) continue;
            Building? storage = job.SiloId is Guid storageId ? _world.Units.FindById(storageId) as Building : null;
            if (storage is null || storage.ArmyId != harvester.ArmyId || !storage.IsCompleted)
            {
                await BeginReturnAsync(harvester, job);
                continue;
            }
            float accepted = storage.StoreResources(harvester.CargoAmount);
            harvester.ApplyHarvestState(HarvestPhase.Unloading, harvester.CargoAmount - accepted);
            if (harvester.ArmyId is Guid armyId && Globals.Game.Armies.Find(armyId) is Army army)
            {
                army.Resources += (int)MathF.Floor(accepted);
                await PublishAsync(new(NetworkMessageType.ArmyResourcesCommand, _networkHandler.LocalPeerId,
                    ArmyId: armyId, ResourceAmount: army.Resources));
            }
            if (!job.ResumeAfterUnload && harvester.CargoAmount <= 0.001f)
            {
                await EndHarvestAsync(harvester);
                continue;
            }
            job.Phase = harvester.CargoAmount > 0.001f ? HarvestPhase.ReturningToSilo : HarvestPhase.DrivingToField;
            harvester.ApplyHarvestState(job.Phase, harvester.CargoAmount);
            job.SiloId = null; job.MoveIssued = false; job.StorageApproach = null;
            await PublishHarvestStateAsync(harvester, job.Phase);
        }
    }

    private bool TryFindTiberium(Point center, out Point cell)
    {
        Point? nearest = _world.Tiberium.Cells.Where(pair => pair.Value.Amount > 0.001f &&
            Math.Abs(pair.Key.X - center.X) <= HarvestSearchRadius && Math.Abs(pair.Key.Y - center.Y) <= HarvestSearchRadius)
            .OrderBy(pair => Math.Abs(pair.Key.X - center.X) + Math.Abs(pair.Key.Y - center.Y))
            .Select(pair => (Point?)pair.Key).FirstOrDefault();
        cell = nearest ?? default;
        return nearest.HasValue;
    }

    private Building? FindNearestSilo(Harvester harvester) => _world.Units.Units.OfType<Building>()
        .Where(silo => silo.ResourceCapacity > 0.0f && silo.AvailableResourceCapacity > 0.001f &&
            silo.ArmyId == harvester.ArmyId && silo.IsCompleted && !silo.IsDying)
        .OrderBy(silo => HorizontalDistanceSquared(harvester.Position, silo.Position)).FirstOrDefault();

    private async Task BeginReturnAsync(Harvester harvester, HarvestJob job)
    {
        job.Phase = HarvestPhase.ReturningToSilo; job.MoveIssued = false;
        job.SiloId = FindNearestSilo(harvester)?.UnitId;
        job.StorageApproach = null;
        await PublishHarvestStateAsync(harvester, job.Phase);
    }

    private async Task EndHarvestAsync(Harvester harvester)
    {
        _harvestJobs.Remove(harvester.UnitId);
        harvester.ApplyHarvestState(HarvestPhase.Idle, harvester.CargoAmount);
        await PublishHarvestStateAsync(harvester, HarvestPhase.Idle);
    }

    private async Task<bool> PublishHarvesterGotoAsync(Harvester harvester, Vector3 target)
    {
        Guid sender = harvester.ArmyId is Guid armyId && Globals.Game.Armies.Find(armyId) is Army army
            ? army.OwnerPlayerIds.FirstOrDefault() : _networkHandler.LocalPeerId;
        NetworkMessage? command = TryCreateGotoCommand(NetworkCommands.CreateGotoRequest(sender,
            [harvester.UnitId], target.X, target.Y, target.Z));
        if (command is null) return false;
        await PublishAsync(command);
        return true;
    }

    private NetworkMessage CreateHarvestStateCommand(Harvester harvester, HarvestPhase phase) =>
        new(NetworkMessageType.HarvestCommand, _networkHandler.LocalPeerId, UnitId: harvester.UnitId,
            HarvestPhase: phase, CargoAmount: harvester.CargoAmount);

    private Task PublishHarvestStateAsync(Harvester harvester, HarvestPhase phase) => PublishAsync(CreateHarvestStateCommand(harvester, phase));

    private async Task PublishAsync(NetworkMessage command)
    {
        _networkHandler.EnqueueLocalMessage(command);
        await _networkHandler.BroadcastAsync(command, CancellationToken.None);
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X, z = first.Z - second.Z;
        return x * x + z * z;
    }

    private NetworkMessage? TryCreateBuildCommand(NetworkMessage request)
    {
        if (string.IsNullOrWhiteSpace(request.UnitTypeId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z) ||
            !float.IsFinite(request.TargetAngleY) || request.X < 0 || request.Z < 0 ||
            request.X >= _world.Terrain.Width - 1 || request.Z >= _world.Terrain.Height - 1)
            return null;
        Player? player = Globals.Game.Players.FirstOrDefault(player => player.Id == request.SenderId);
        if (player is null || Globals.Game.Armies.Find(player.ArmyId) is not Army army)
            return null;
        Guid armyId = player.ArmyId;
        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Building, request.UnitTypeId, armyId));
        if (!quote.IsAvailable || !quote.CanAfford(army.Resources))
            return null;

        Guid unitId = request.UnitId ?? Guid.NewGuid();
        if (unitId == Guid.Empty || _world.Units.FindById(unitId) is not null)
            return null;
        Vector3 position = new(request.X, _world.Terrain.GetHeight((int)request.X, (int)request.Z), request.Z);
        // Register immediately so another request in this host tick cannot
        // claim the same footprint before the replicated command is processed.
        Building? building = _world.Units.SpawnBuilding(request.UnitTypeId, position,
            request.TargetAngleY, unitId, request.SenderId, quote.FinalPrice);
        if (building is null)
            return null;
        ArmyResourceService.TrySpend(army, _world, building.PurchasePrice);
        return NetworkCommands.CreateBuildCommand(_networkHandler.LocalPeerId,
            request with { UnitId = unitId, PlayerId = request.SenderId, Y = position.Y,
                ArmyId = armyId, ResourceAmount = army.Resources, PurchasePrice = quote.FinalPrice });
    }

    private NetworkMessage? TryCreateSellBuildingCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            _world.Units.FindById(buildingId) is not Building building ||
            building is GenericBuilding || building.IsDying || !building.IsCompleted ||
            building.ArmyId is not Guid armyId ||
            Globals.Game.Players.FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId != armyId ||
            building.Occupancy?.Occupants.Count > 0 ||
            Globals.Game.Armies.Find(armyId) is not Army army)
        {
            return null;
        }

        army.Resources += building.SellRefund;
        _world.Units.SellBuilding(buildingId);
        return NetworkCommands.CreateSellBuildingCommand(
            _networkHandler.LocalPeerId, building, army.Resources);
    }

    private NetworkMessage? TryCreateCancelConstructionCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            _world.Units.FindById(buildingId) is not Building building ||
            building is GenericBuilding || building.IsDying || building.IsCompleted ||
            building.ArmyId is not Guid armyId ||
            Globals.Game.Players.FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId != armyId ||
            Globals.Game.Armies.Find(armyId) is not Army army)
        {
            return null;
        }

        army.Resources += building.CancelRefund;
        _world.Units.SellBuilding(buildingId);
        return NetworkCommands.CreateCancelConstructionCommand(
            _networkHandler.LocalPeerId, building, army.Resources);
    }

    private NetworkMessage? TryCreateDestroyBuildingCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            _world.Units.FindById(buildingId) is not Building building ||
            building.IsDying ||
            building.ArmyId is not Guid armyId ||
            Globals.Game.Players.FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId != armyId)
        {
            return null;
        }

        return NetworkCommands.CreateDestroyUnitCommand(_networkHandler.LocalPeerId, buildingId);
    }

    private NetworkMessage? RememberStartPositionWish(NetworkMessage request)
    {
        if (request.StartPositionSlot is not int slot ||
            !Globals.Game.Players.Any(player => player.Id == request.SenderId) ||
            !_world.GameplayMarkers.Markers.Any(marker =>
                marker.Type == GameplayMarkerType.PlayerStart && marker.PlayerSlot == slot))
        {
            return null;
        }

        _startPositionWishes[request.SenderId] = slot;
        Globals.Console?.Print($"Player {request.SenderId.ToString("N")[..8]} requested start position {slot}.");
        return null;
    }

    private NetworkMessage? TryCreateStartMultiplayerGameCommand(NetworkMessage request)
    {
        if (request.SenderId != _networkHandler.LocalPeerId)
            return null;

        // AI controllers live only on the host. Include their player identities
        // explicitly so a map publish or player-list rebuild cannot drop them
        // from the following match.
        Player[] players = Globals.Game.Players
            .Concat(Globals.Game.AIPlayers.Select(ai => ai.Player))
            .DistinctBy(player => player.Id)
            .OrderBy(player => player.Id)
            .ToArray();
        List<GameplayMarker> starts = _world.GameplayMarkers.Markers
            .Where(marker => marker.Type == GameplayMarkerType.PlayerStart && marker.PlayerSlot is not null)
            .OrderBy(marker => marker.PlayerSlot)
            .ThenBy(marker => marker.Id)
            .ToList();
        if (players.Length == 0 || starts.Count < players.Length)
        {
            Globals.Console?.Print($"Cannot start game: {players.Length} player(s), but only {starts.Count} start position(s).");
            return null;
        }

        List<GameplayMarker> free = [.. starts];
        Dictionary<Guid, GameplayMarker> assigned = [];
        foreach (Player player in players)
        {
            if (!_startPositionWishes.TryGetValue(player.Id, out int slot))
                continue;
            GameplayMarker? wished = free.FirstOrDefault(marker => marker.PlayerSlot == slot);
            if (wished is null)
                continue;
            assigned[player.Id] = wished;
            free.Remove(wished);
        }

        for (int index = free.Count - 1; index > 0; index--)
        {
            int swap = Random.Shared.Next(index + 1);
            (free[index], free[swap]) = (free[swap], free[index]);
        }

        foreach (Player player in players)
            if (!assigned.ContainsKey(player.Id))
            {
                assigned[player.Id] = free[0];
                free.RemoveAt(0);
            }

        MatchStartAssignment[] assignments = players.Select(player =>
        {
            GameplayMarker marker = assigned[player.Id];
            float y = _world.Terrain.GetHeight((int)marker.Position.X, (int)marker.Position.Z);
            return new MatchStartAssignment(
                player.Id, player.ArmyId, marker.PlayerSlot!.Value,
                marker.Position.X, y, marker.Position.Z, marker.RotationDegrees, Guid.NewGuid(),
                Globals.Game.AIPlayers.Any(ai => ai.Id == player.Id));
        }).ToArray();
        _startPositionWishes.Clear();
        return NetworkCommands.CreateStartMultiplayerGameCommand(
            _networkHandler.LocalPeerId, assignments);
    }

    private NetworkMessage? TryCreateSetRallyPointCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid unitId || request.RallyPoint is not RallyPointState requested ||
            _world.Units.FindById(unitId) is not Unit unit || !unit.SupportsRallyPoint || unit.IsDying ||
            !Globals.Game.Armies.CanControl(request.SenderId, unit.ArmyId))
            return null;

        Vector3? position = null;
        if (requested.HasPosition)
        {
            if (!float.IsFinite(requested.X) || !float.IsFinite(requested.Y) || !float.IsFinite(requested.Z) ||
                requested.X < 0 || requested.Z < 0 ||
                requested.X >= _world.Terrain.Width - 1 || requested.Z >= _world.Terrain.Height - 1)
                return null;
            Point cell = _world.GameGrid.ToCell(new Vector3(requested.X, 0, requested.Z));
            if (!_world.GameGrid.Contains(cell))
                return null;
            GridCell data = _world.GameGrid.GetCell(cell);
            if (!data.HasTerrain || data.IsBlocked || data.ExcludeFromPathfinding ||
                data.AllowedMovement == MovementModes.None || _world.GameGrid.GetOccupant(cell) is Building)
                return null;
            position = new Vector3(requested.X, _world.Terrain.GetHeight((int)requested.X, (int)requested.Z), requested.Z);
        }
        unit.SetRallyPoint(position);
        return NetworkCommands.CreateSetRallyPointCommand(_networkHandler.LocalPeerId, unit);
    }

    private NetworkMessage? TryCreateTrainUnitCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            string.IsNullOrWhiteSpace(request.UnitTypeId) ||
            _world.Units.FindById(buildingId) is not Building building ||
            !building.IsCompleted ||
            !Globals.Game.Armies.CanControl(request.SenderId, building.ArmyId) ||
            !building.TryGetProductionDuration(request.UnitTypeId, out float durationSeconds) ||
            building.ArmyId is not Guid armyId ||
            Globals.Game.Armies.Find(armyId) is not Army army)
        {
            return null;
        }

        if (building is Helipad pad && !pad.CanOrderHelicopter(_world)) return null;

        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Unit, request.UnitTypeId, armyId, building.UnitId));
        if (!quote.IsAvailable || !quote.CanAfford(army.Resources)) return null;

        Guid orderId = request.ProductionOrderId ?? Guid.NewGuid();
        Guid requestedByPlayerId = request.PlayerId ?? request.SenderId;
        if (!building.TryQueueProduction(
                orderId,
                request.UnitTypeId,
                requestedByPlayerId,
                durationSeconds))
        {
            return null;
        }

        ArmyResourceService.TrySpend(army, _world, quote.FinalPrice);

        return NetworkCommands.CreateTrainUnitCommand(
            _networkHandler.LocalPeerId,
            request with { ProductionOrderId = orderId },
            durationSeconds,
            armyId,
            army.Resources);
    }

    private NetworkMessage? TryCreateResearchCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            string.IsNullOrWhiteSpace(request.UnitTypeId) ||
            _world.Units.FindById(buildingId) is not GDIBase building ||
            !building.IsCompleted ||
            !ResearchProjects.TryGetGrantedPerk(request.UnitTypeId, out PerkType perk) ||
            !Globals.Game.Armies.CanControl(request.SenderId, building.ArmyId) ||
            !building.TryGetProductionDuration(request.UnitTypeId, out float durationSeconds) ||
            building.ArmyId is not Guid armyId ||
            Globals.Game.Armies.Find(armyId) is not Army army ||
            army.Perks.Has(perk) ||
            _world.Units.Units.OfType<Building>().Any(candidate => candidate.ArmyId == armyId &&
                candidate.ProductionQueue.Orders.Any(order => string.Equals(
                    order.UnitTypeId, request.UnitTypeId, StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        PurchaseQuote quote = Globals.Game.Pricing.GetQuote(new PurchaseRequest(
            PurchasableType.Research, request.UnitTypeId, armyId, building.UnitId));
        if (!quote.IsAvailable || !quote.CanAfford(army.Resources)) return null;

        Guid orderId = request.ProductionOrderId ?? Guid.NewGuid();
        Guid requestedByPlayerId = request.PlayerId ?? request.SenderId;
        if (!building.TryQueueProduction(orderId, request.UnitTypeId,
                requestedByPlayerId, durationSeconds))
            return null;

        ArmyResourceService.TrySpend(army, _world, quote.FinalPrice);
        return NetworkCommands.CreateResearchCommand(_networkHandler.LocalPeerId,
            request with { ProductionOrderId = orderId }, durationSeconds, armyId, army.Resources);
    }

    private NetworkMessage? TryCreateEnterUnitCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid occupantId ||
            request.TargetId is not Guid containerId ||
            _world.Units.FindById(occupantId) is not MobileUnit occupant ||
            _world.Units.FindById(containerId) is not Unit container ||
            container.Occupancy is not OccupancyComponent occupancy ||
            !Globals.Game.Armies.CanControl(request.SenderId, occupant.ArmyId) ||
            !occupancy.TryReserve(occupant, request.OccupantRole, out OccupantRole role))
        {
            return null;
        }

        return NetworkCommands.CreateEnterUnitCommand(_networkHandler.LocalPeerId, request, role);
    }

    private NetworkMessage? TryCreateLeaveContainerCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid containerId ||
            _world.Units.FindById(containerId) is not Unit container ||
            container is Helicopter { IsLanded: false } ||
            container.Occupancy?.GetPreferredOccupantToLeave() is not Guid occupantId ||
            _world.Units.FindById(occupantId) is not MobileUnit occupant ||
            !Globals.Game.Armies.CanControl(request.SenderId, container.ArmyId) ||
            !TryFindDisembarkPosition(container, occupant, out Vector3 exitPosition) ||
            !_world.Units.DisembarkUnit(containerId, occupantId, exitPosition))
        {
            return null;
        }

        return NetworkCommands.CreateLeaveContainerCommand(
            _networkHandler.LocalPeerId,
            containerId,
            occupantId,
            exitPosition);
    }

    /// <summary>
    /// Keeps explicit team changes for known players. A joining player receives
    /// the first free positive team number, so everyone begins as an opponent.
    /// </summary>
    private static int ConfirmTeamId(NetworkMessage request)
    {
        Guid playerId = request.PlayerId ?? request.SenderId;
        if (Globals.Game.Players.Any(player => player.Id == playerId))
            return request.TeamId > 0 ? request.TeamId : Globals.Game.GetNextAvailableTeamId();
        return Globals.Game.GetNextAvailableTeamId();
    }

    /// <summary>Grants the requested skin unless another player already owns it.</summary>
    private static PlayerSkin ConfirmPlayerSkin(NetworkMessage request)
    {
        Guid playerId = request.PlayerId ?? request.SenderId;
        Player[] otherPlayers = Globals.Game.Players.Where(player => player.Id != playerId).ToArray();
        PlayerSkin requested = (PlayerSkin)(request.PlayerSkin ?? (int)PlayerSkin.Green);
        bool IsValid(PlayerSkin skin) => Enum.IsDefined(skin);
        bool IsTaken(PlayerSkin skin) => otherPlayers.Any(player => player.Skin == skin);

        if (IsValid(requested) && !IsTaken(requested))
            return requested;

        foreach (SkinHandler.SkinDefinition definition in Globals.SkinHandler.Skins)
            if (!IsTaken(definition.Skin))
                return definition.Skin;

        return PlayerSkin.Green;
    }

    private void UpdateHostSimulation(GameTime gameTime)    {
        if (!_networkHandler.IsHost)
            return;

        double elapsedSeconds = gameTime.ElapsedGameTime.TotalSeconds;
        _hostTime += elapsedSeconds;
        _simulationAccumulator += elapsedSeconds;

        while (_simulationAccumulator >= HostSimulationInterval)
        {
            _simulationAccumulator -= HostSimulationInterval;
            foreach (var unit in _world.Units.Units.OfType<Unit>())
            {
                if (unit is Helicopter helicopter) helicopter.SimulateFlight(_world, (float)HostSimulationInterval);
                else unit.UpdateHost(gameTime);
            }

            _earthworks.Update((float)HostSimulationInterval);
            UpdateProduction((float)HostSimulationInterval);
            UpdateContainerEntries();
            SimulateHostProjectiles((float)HostSimulationInterval);
            UpdateDefensiveTargets();

            foreach (Unit unit in _world.Units.Units.OfType<Unit>())
            {
                if (!unit.IsReadyToShoot(_hostTime))
                    continue;
                if (!unit.TryQueueShot(_hostTime, out Unit? target) || target is null)
                    continue;
                Vector3 aimPosition = target.Position + Vector3.Up * (target.Height * 0.5f);
                _requestQueue.Enqueue(NetworkCommands.CreateAttackRequest(
                    _networkHandler.LocalPeerId,
                    [unit.UnitId],
                    aimPosition.X, aimPosition.Y, aimPosition.Z));
            }

            foreach (Unit unit in _world.Units.Units.OfType<Unit>())
            {
                if (!unit.IsReadyToShoot(_hostTime))
                    continue;
                if (!unit.TryQueueGroundShot(_hostTime, out Vector3 target))
                    continue;
                _requestQueue.Enqueue(NetworkCommands.CreateAttackRequest(
                    _networkHandler.LocalPeerId, [unit.UnitId], target.X, target.Y, target.Z));
            }

            foreach (TiberiumSource source in _world.Units.Units.OfType<TiberiumSource>())
            {
                if (!source.TryTakePendingSeedCell(out Point cell) ||
                    !_world.Tiberium.TryHostSeed(cell, _hostTime, out TiberiumSeedState state))
                    continue;
                NetworkMessage seedCommand = NetworkCommands.CreateTiberiumSeedCommand(_networkHandler.LocalPeerId, state);
                _networkHandler.EnqueueLocalMessage(seedCommand);
                _ = _networkHandler.BroadcastAsync(seedCommand, CancellationToken.None);
            }
        }

        int sentUpdates = 0;
        foreach (Building constructionSite in _world.Units.Units
            .OfType<Building>()
            .Where(site => site.NetworkStateDirty)
            .Concat(_world.Units.Units
                .OfType<Building>()
                .Where(site => !site.NetworkStateDirty && site.IsNetworkUpdateDue(_hostTime))))
        {
            if (sentUpdates >= MaximumStateUpdatesPerTick)
                break;

            NetworkMessage state = NetworkCommands.CreateUnitStateCommand(
                _networkHandler.LocalPeerId,
                constructionSite.GetState());
            _networkHandler.EnqueueLocalMessage(state);
            _ = _networkHandler.BroadcastAsync(state, CancellationToken.None);
            constructionSite.MarkNetworkStateSent(_hostTime, StateHeartbeatInterval);
            sentUpdates++;
        }
    }

    private void HandleMedicCommandOverride(NetworkMessage request)
    {
        bool stop = request.Type == NetworkMessageType.StopRequest ||
            request.Type == NetworkMessageType.UnitActionRequest && request.UnitActionType == UnitActionType.Stop;
        bool overrideMovement = stop || request.Type is NetworkMessageType.GotoRequest or
            NetworkMessageType.FollowRequest or NetworkMessageType.AttackTargetRequest or
            NetworkMessageType.AttackGroundRequest or NetworkMessageType.MoveAwayRequest;
        if (!overrideMovement)
            return;

        IEnumerable<Guid> ids = ExpandSquadUnitIds(request.UnitIds ?? []);
        if (request.Type == NetworkMessageType.MoveAwayRequest && request.UnitId is Guid singleId)
            ids = ids.Append(singleId);
        foreach (Guid id in ids.Distinct())
        {
            if (_world.Units.FindById(id) is not Medic medic ||
                !Globals.Game.Armies.CanControl(request.SenderId, medic.ArmyId))
                continue;
            _medicJobs.Remove(id);
            if (stop)
                _medicsHoldingPosition.Add(id);
            else
                _medicsHoldingPosition.Remove(id);
        }
    }

    private async Task UpdateAndPublishMedicsAsync()
    {
        Medic[] medics = _world.Units.Units.OfType<Medic>()
            .Where(medic => !medic.IsDying && !medic.IsEmbarked)
            .ToArray();
        HashSet<Guid> activeMedicIds = medics.Select(medic => medic.UnitId).ToHashSet();
        foreach (Guid id in _medicJobs.Keys.Where(id => !activeMedicIds.Contains(id)).ToArray())
            _medicJobs.Remove(id);
        _medicsHoldingPosition.RemoveWhere(id => !activeMedicIds.Contains(id));

        Soldier[] soldiers = _world.Units.Units.OfType<Soldier>()
            .Where(patient => !patient.IsDying && !patient.IsEmbarked && patient.HitPoints > 0.0f)
            .ToArray();
        float healingRadius = Medic.HealingRadiusInCells * _world.GameGrid.CellSize;
        float healingRadiusSquared = healingRadius * healingRadius;
        float searchRadius = Medic.SearchRadiusInCells * _world.GameGrid.CellSize;
        float searchRadiusSquared = searchRadius * searchRadius;

        foreach (Medic medic in medics)
        {
            SquadLeader? leader = medic.SquadLeaderId is Guid leaderId &&
                _world.Units.FindById(leaderId) is SquadLeader candidateLeader &&
                !candidateLeader.IsDying && !candidateLeader.IsEmbarked && candidateLeader.ArmyId == medic.ArmyId
                    ? candidateLeader
                    : null;
            if (leader is not null)
            {
                _medicJobs.Remove(medic.UnitId);
                Soldier? squadPatient = SelectMedicPatient(medic, soldiers, healingRadiusSquared,
                    patient => patient.UnitId == leader.UnitId || patient.SquadLeaderId == leader.UnitId);
                if (squadPatient is not null)
                    await HealPatientAsync(medic, squadPatient);
                continue;
            }

            bool holdPosition = _medicsHoldingPosition.Contains(medic.UnitId);
            float acquisitionRadiusSquared = holdPosition ? healingRadiusSquared : searchRadiusSquared;
            MedicJob? job = _medicJobs.GetValueOrDefault(medic.UnitId);
            Soldier? patient = job is null ? null : soldiers.FirstOrDefault(candidate => candidate.UnitId == job.PatientId);
            if (!IsValidMedicPatient(medic, patient, acquisitionRadiusSquared))
            {
                bool cancelAutomaticMove = job?.MoveIssued == true && medic.CurrentCommand is not null;
                _medicJobs.Remove(medic.UnitId);
                job = null;
                patient = null;
                if (cancelAutomaticMove)
                {
                    NetworkMessage stop = CreateStopCommand(NetworkCommands.CreateStopRequest(
                        GetMedicCommandSender(medic), [medic.UnitId]));
                    await PublishAsync(stop);
                    continue;
                }
            }

            if (job is null)
            {
                if (medic.CurrentCommand is not null)
                    continue;
                patient = SelectMedicPatient(medic, soldiers, acquisitionRadiusSquared, _ => true);
                if (patient is null)
                    continue;
                job = new MedicJob(patient.UnitId);
                _medicJobs[medic.UnitId] = job;
            }

            float distanceSquared = HorizontalDistanceSquared(medic.Position, patient!.Position);
            if (distanceSquared <= healingRadiusSquared)
            {
                if (job.MoveIssued && medic.CurrentCommand is not null)
                {
                    NetworkMessage stop = CreateStopCommand(NetworkCommands.CreateStopRequest(
                        GetMedicCommandSender(medic), [medic.UnitId]));
                    await PublishAsync(stop);
                }
                job.MoveIssued = false;
                await HealPatientAsync(medic, patient);
                continue;
            }

            if (holdPosition)
            {
                _medicJobs.Remove(medic.UnitId);
                continue;
            }
            if (medic.CurrentCommand is not null && !job.MoveIssued)
            {
                _medicJobs.Remove(medic.UnitId);
                continue;
            }
            if (!job.MoveIssued || medic.CurrentCommand is null)
            {
                NetworkMessage? move = TryCreateGotoCommand(NetworkCommands.CreateGotoRequest(
                    GetMedicCommandSender(medic), [medic.UnitId], patient.Position.X, patient.Position.Y, patient.Position.Z));
                if (move is null)
                {
                    _medicJobs.Remove(medic.UnitId);
                    continue;
                }
                await PublishAsync(move);
                job.MoveIssued = true;
            }
        }
    }

    private static bool IsValidMedicPatient(Medic medic, Soldier? patient, float radiusSquared) =>
        patient is not null && !patient.IsDying && !patient.IsEmbarked && patient.HitPoints > 0.0f &&
        patient.HitPoints < patient.MaxHitPoints && patient.ArmyId == medic.ArmyId &&
        HorizontalDistanceSquared(medic.Position, patient.Position) <= radiusSquared;

    private static Soldier? SelectMedicPatient(
        Medic medic, IEnumerable<Soldier> soldiers, float radiusSquared, Func<Soldier, bool> filter) =>
        soldiers.Where(patient => filter(patient) && IsValidMedicPatient(medic, patient, radiusSquared))
            .OrderBy(patient => patient.HitPoints / Math.Max(1.0f, patient.MaxHitPoints))
            .ThenBy(patient => HorizontalDistanceSquared(medic.Position, patient.Position))
            .ThenBy(patient => patient.UnitId)
            .FirstOrDefault();

    private Guid GetMedicCommandSender(Medic medic) => medic.ArmyId is Guid armyId &&
        Globals.Game.Armies.Find(armyId) is Army army && army.OwnerPlayerIds.Count > 0
            ? army.OwnerPlayerIds.OrderBy(id => id).First()
            : _networkHandler.LocalPeerId;

    private async Task HealPatientAsync(Medic medic, Soldier patient)
    {
        if (_nextPatientHealTimes.GetValueOrDefault(patient.UnitId) > _hostTime)
            return;
        float healed = patient.Heal(Medic.HealAmountPerPulse);
        if (healed <= 0.0f)
            return;
        _nextPatientHealTimes[patient.UnitId] = _hostTime + Medic.HealPulseSeconds;
        await PublishAsync(NetworkCommands.CreateUnitHitCommand(
            _networkHandler.LocalPeerId, patient.UnitId, medic.UnitId, patient.Position, -healed, patient.HitPoints));
    }

    private void UpdateProduction(float elapsedSeconds)
    {
        foreach (Building building in _world.Units.Units.OfType<Building>().ToArray())
        {
            if (building is TiberiumRefinery refinery && refinery.IsCompleted && !refinery.IncludedUnitGranted)
            {
                Guid ownerPlayerId = refinery.ArmyId is Guid armyId
                    ? Globals.Game.Armies.Find(armyId)?.OwnerPlayerIds.OrderBy(id => id).FirstOrDefault() ?? Guid.Empty
                    : refinery.CreatorPlayerId;
                refinery.TryQueueIncludedHarvester(ownerPlayerId);
            }
            if (building is Helipad includedPad && includedPad.IsCompleted && !includedPad.IncludedUnitGranted)
            {
                Guid ownerPlayerId = includedPad.ArmyId is Guid armyId
                    ? Globals.Game.Armies.Find(armyId)?.OwnerPlayerIds.OrderBy(id => id).FirstOrDefault() ?? Guid.Empty
                    : includedPad.CreatorPlayerId;
                includedPad.TryQueueIncludedHelicopter(ownerPlayerId);
            }
            if (!building.UpdateProduction(elapsedSeconds, out ProductionOrder? completedOrder) ||
                completedOrder is null)
            {
                continue;
            }

            if (ResearchProjects.TryGetGrantedPerk(completedOrder.UnitTypeId, out PerkType researchPerk) &&
                building.ArmyId is Guid researchArmyId &&
                Globals.Game.Armies.Find(researchArmyId) is Army researchArmy)
            {
                researchArmy.Perks.GrantPermanent(researchPerk, completedOrder.OrderId);
                NetworkMessage researchCompleted = NetworkCommands.CreateResearchCompletedCommand(
                    _networkHandler.LocalPeerId, researchArmyId, completedOrder.OrderId,
                    completedOrder.UnitTypeId);
                _networkHandler.EnqueueLocalMessage(researchCompleted);
                _ = _networkHandler.BroadcastAsync(researchCompleted, CancellationToken.None);
                continue;
            }

            if (building is Helipad pad)
            {
                pad.DeliveryPending = true;
                Vector3 landing = pad.GetLandingSurfacePosition();
                NetworkMessage delivery = NetworkCommands.CreateProducedUnitCommand(
                    _networkHandler.LocalPeerId, building, completedOrder,
                    landing + Vector3.Up * 30f, landing);
                _networkHandler.EnqueueLocalMessage(delivery);
                _ = _networkHandler.BroadcastAsync(delivery, CancellationToken.None);
                continue;
            }

            Vector3 spawnPosition = building.TryGetProductionSpawnPosition(out Vector3 spawnPivot)
                ? spawnPivot
                : building.Position + Vector3.Up * 0.1f;

            Vector3 exitPosition;
            if (!building.TryGetProductionExitPosition(out exitPosition))
            {
                Vector3 forward = building.Transform.Forward;
                forward.Y = 0.0f;
                if (forward.LengthSquared() <= 0.0001f)
                    forward = Vector3.Forward;
                else
                    forward.Normalize();

                float exitDistance =
                    Math.Max(building.Width, building.Length) * _world.GameGrid.CellSize * 0.5f +
                    _world.GameGrid.CellSize * 1.5f;
                exitPosition = building.Position + forward * exitDistance;
            }

            int terrainX = Math.Clamp((int)MathF.Floor(exitPosition.X), 0, _world.Terrain.Width - 1);
            int terrainZ = Math.Clamp((int)MathF.Floor(exitPosition.Z), 0, _world.Terrain.Height - 1);
            exitPosition.Y = _world.Terrain.GetHeight(terrainX, terrainZ);

            MobileUnit? producedShape = UnitFactory.SpawnUnit(completedOrder.UnitTypeId,
                spawnPosition, 0.0f, Guid.NewGuid(), completedOrder.RequestedByPlayerId);
            if (producedShape is not null &&
                ProductionExitResolver.TryResolve(_world, building, producedShape,
                    spawnPosition, exitPosition, out Vector3 resolvedExit))
                exitPosition = resolvedExit;

            NetworkMessage command = NetworkCommands.CreateProducedUnitCommand(
                _networkHandler.LocalPeerId,
                building,
                completedOrder,
                spawnPosition,
                exitPosition);
            _networkHandler.EnqueueLocalMessage(command);
            _ = _networkHandler.BroadcastAsync(command, CancellationToken.None);
        }
    }

    private void UpdateContainerEntries()
    {
        foreach (MobileUnit occupant in _world.Units.Units.OfType<MobileUnit>().ToArray())
        {
            if (occupant.PendingEnterContainerId is not Guid containerId ||
                _world.Units.FindById(containerId) is not Unit container ||
                container.Occupancy is not OccupancyComponent occupancy ||
                !occupancy.IsReservedBy(occupant.UnitId))
            {
                continue;
            }

            container.TryGetEntryWorldPosition(out Vector3 entrancePosition);
            Vector2 offset = new(
                entrancePosition.X - occupant.Position.X,
                entrancePosition.Z - occupant.Position.Z);
            float arrivalDistance = Math.Max(1.25f, _world.GameGrid.CellSize * 0.75f);
            if (offset.LengthSquared() > arrivalDistance * arrivalDistance)
                continue;

            if (!occupancy.TryGetReservedRole(occupant.UnitId, out OccupantRole role))
                continue;
            if (!_world.Units.EmbarkUnit(occupant.UnitId, container.UnitId, role))
                continue;

            NetworkMessage command = NetworkCommands.CreateEmbarkUnitCommand(
                _networkHandler.LocalPeerId,
                occupant.UnitId,
                container.UnitId,
                role);
            _networkHandler.EnqueueLocalMessage(command);
            _ = _networkHandler.BroadcastAsync(command, CancellationToken.None);
        }
    }

    private bool TryFindDisembarkPosition(
        Unit container,
        MobileUnit occupant,
        out Vector3 exitPosition)
    {
        container.TryGetExitWorldPosition(out Vector3 preferredPosition);
        Point preferredCell = _world.GameGrid.ToCell(preferredPosition);

        for (int radius = 0; radius <= 4; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (radius > 0 && Math.Abs(x) != radius && Math.Abs(y) != radius)
                        continue;

                    Point cell = new(preferredCell.X + x, preferredCell.Y + y);
                    if (!_world.GameGrid.CanPlace(occupant, cell))
                        continue;

                    exitPosition = radius == 0
                        ? preferredPosition
                        : _world.GameGrid.ToWorldPosition(cell, 0.0f);
                    int terrainX = Math.Clamp((int)MathF.Floor(exitPosition.X), 0, _world.Terrain.Width - 1);
                    int terrainZ = Math.Clamp((int)MathF.Floor(exitPosition.Z), 0, _world.Terrain.Height - 1);
                    exitPosition.Y = _world.Terrain.GetHeight(terrainX, terrainZ);
                    return true;
                }
            }
        }

        exitPosition = Vector3.Zero;
        return false;
    }

    private void UpdateDefensiveTargets()
    {
        Unit[] units = _world.Units.Units.OfType<Unit>().ToArray();
        foreach (Unit defender in units)
        {
            // Player-issued targets always win over automatic defense.
            if (defender.HasExplicitTarget || defender.Behavior == UnitBehavior.Passive)
            {
                if (defender.ClearTemporaryTarget())
                    PublishTemporaryTarget(defender, null);
                continue;
            }

            Unit? currentTarget = defender.TemporaryTargetUnitId is Guid currentTargetId
                ? _world.Units.FindById(currentTargetId)
                : null;
            if (currentTarget is not null &&
                defender.ShouldAttack(currentTarget) &&
                IsWithinAttackRange(defender, currentTarget))
            {
                continue;
            }

            Unit? newTarget = units
                .Where(candidate => defender.ShouldAttack(candidate))
                .Where(candidate => IsWithinAttackRange(defender, candidate))
                .OrderBy(candidate => HorizontalDistanceSquared(defender, candidate))
                .FirstOrDefault();

            if (newTarget is not null)
            {
                if (defender.SetTemporaryTarget(newTarget.UnitId))
                    PublishTemporaryTarget(defender, newTarget.UnitId);
            }
            else if (defender.ClearTemporaryTarget())
            {
                PublishTemporaryTarget(defender, null);
            }
        }
    }

    private static bool IsWithinAttackRange(Unit attacker, Unit target) =>
        HorizontalDistanceSquared(attacker, target) <= attacker.AttackRange * attacker.AttackRange;

    private static float HorizontalDistanceSquared(Unit first, Unit second)
    {
        float x = first.Position.X - second.Position.X;
        float z = first.Position.Z - second.Position.Z;
        return x * x + z * z;
    }

    private void PublishTemporaryTarget(Unit unit, Guid? targetId)
    {
        NetworkMessage command = NetworkCommands.CreateTemporaryTargetCommand(
            _networkHandler.LocalPeerId,
            unit.UnitId,
            targetId);
        _networkHandler.EnqueueLocalMessage(command);
        _ = _networkHandler.BroadcastAsync(command, CancellationToken.None);
    }

    private async Task ResolveGroundAttackAsync(NetworkMessage request)
    {
        Vector3 impactPosition = new(request.X, request.Y, request.Z);

        foreach (Guid attackerId in request.UnitIds ?? Array.Empty<Guid>())
        {
            if (_world.Units.FindById(attackerId) is not Unit attacker || attacker.IsDying)
                continue;

            if (attacker.ProjectileKind == ProjectileKind.Rocket)
            {
                Vector3 launchPosition = attacker.TryGetProjectileLaunchWorldTransform(out Matrix launchTransform)
                    ? launchTransform.Translation
                    : attacker.Position + Vector3.Up * (attacker.Height * 0.75f);
                ProjectileFlightProfile profile = ProjectileFlightProfile.For(ProjectileKind.Rocket);
                Vector3 launchDirection = CalculateRocketLaunchDirection(
                    launchPosition,
                    impactPosition,
                    attacker.ProjectileSpeed,
                    profile,
                    attacker.Transform.Forward);
                Vector3 initialVelocity = launchDirection * attacker.ProjectileSpeed;
                Guid projectileId = Guid.NewGuid();
                _hostProjectiles.Add(new HostProjectile(
                    projectileId,
                    attackerId,
                    launchPosition,
                    initialVelocity,
                    ProjectileKind.Rocket,
                    attacker.AttackDamage,
                    attacker.AttackDamageType));

                NetworkMessage spawnCommand = NetworkCommands.CreateProjectileSpawnCommand(
                    _networkHandler.LocalPeerId,
                    projectileId,
                    attackerId,
                    ProjectileKind.Rocket,
                    launchPosition,
                    initialVelocity,
                    _hostTime);
                _networkHandler.EnqueueLocalMessage(spawnCommand);
                await _networkHandler.BroadcastAsync(spawnCommand, CancellationToken.None);
                continue;
            }

            if (attacker.UsesHitscanWeapon)
            {
                NetworkMessage impactCommand = NetworkCommands.CreateBulletImpactCommand(
                    _networkHandler.LocalPeerId, attackerId, impactPosition);
                _networkHandler.EnqueueLocalMessage(impactCommand);
                await _networkHandler.BroadcastAsync(impactCommand, CancellationToken.None);
            }

            await ApplyImpactDamageAsync(attackerId, impactPosition,
                attacker.AttackDamage, attacker.AttackDamageType);
        }
    }

    private void SimulateHostProjectiles(float elapsedSeconds)
    {
        for (int index = _hostProjectiles.Count - 1; index >= 0; index--)
        {
            HostProjectile projectile = _hostProjectiles[index];
            ProjectileTrajectory.Evaluate(
                projectile.Start,
                projectile.InitialVelocity,
                projectile.Age,
                projectile.Profile,
                out Vector3 start,
                out _);
            float nextAge = projectile.Age + elapsedSeconds;
            ProjectileTrajectory.Evaluate(
                projectile.Start,
                projectile.InitialVelocity,
                nextAge,
                projectile.Profile,
                out Vector3 end,
                out _);

            bool collided = TryFindProjectileCollision(
                projectile.AttackerId,
                start,
                end,
                out Vector3 impactPosition,
                out Vector3 impactNormal,
                out Guid? hitUnitId);
            if (collided || nextAge >= projectile.Profile.MaximumLifetime)
            {
                if (!collided)
                {
                    impactPosition = end;
                    impactNormal = Vector3.Up;
                }

                _projectileImpacts.Add(new ProjectileImpact(
                    projectile.ProjectileId,
                    projectile.AttackerId,
                    impactPosition,
                    impactNormal,
                    hitUnitId,
                    projectile.Damage,
                    projectile.DamageType,
                    projectile.Profile.ExplosionRadius));
                _hostProjectiles.RemoveAt(index);
                continue;
            }

            projectile.Age = nextAge;
        }
    }

    private static Vector3 CalculateRocketLaunchDirection(
        Vector3 start,
        Vector3 target,
        float initialSpeed,
        ProjectileFlightProfile profile,
        Vector3 fallbackDirection)
    {
        Vector2 horizontalOffset = new(target.X - start.X, target.Z - start.Z);
        float averagePoweredSpeed = Math.Max(
            0.1f,
            initialSpeed + profile.MotorAcceleration * profile.MotorBurnDuration * 0.5f);
        float estimatedFlightTime = horizontalOffset.Length() / averagePoweredSpeed;
        Vector3 compensatedTarget = target + Vector3.Up *
            (0.5f * profile.Gravity * estimatedFlightTime * estimatedFlightTime);
        Vector3 direction = compensatedTarget - start;
        if (direction.LengthSquared() > 0.0001f)
            return Vector3.Normalize(direction);

        fallbackDirection.Y = 0.0f;
        return fallbackDirection.LengthSquared() > 0.0001f
            ? Vector3.Normalize(fallbackDirection)
            : Vector3.Forward;
    }

    private async Task PublishProjectileImpactsAsync()
    {
        if (_projectileImpacts.Count == 0)
            return;

        ProjectileImpact[] impacts = [.. _projectileImpacts];
        _projectileImpacts.Clear();
        foreach (ProjectileImpact impact in impacts)
        {
            NetworkMessage impactCommand = NetworkCommands.CreateProjectileImpactCommand(
                _networkHandler.LocalPeerId,
                impact.ProjectileId,
                impact.AttackerId,
                impact.Position,
                impact.Normal,
                impact.HitUnitId,
                _hostTime);
            _networkHandler.EnqueueLocalMessage(impactCommand);
            await _networkHandler.BroadcastAsync(impactCommand, CancellationToken.None);
            await ApplyExplosionDamageAsync(impact);
        }
    }

    private bool TryFindProjectileCollision(
        Guid attackerId,
        Vector3 start,
        Vector3 end,
        out Vector3 position,
        out Vector3 normal,
        out Guid? hitUnitId)
    {
        float closestT = float.MaxValue;
        position = default;
        normal = Vector3.Up;
        hitUnitId = null;

        Vector3 segment = end - start;
        float distance = segment.Length();
        int samples = Math.Max(1, (int)MathF.Ceiling(distance / 0.2f));
        for (int sample = 1; sample <= samples; sample++)
        {
            float t = (float)sample / samples;
            Vector3 point = Vector3.Lerp(start, end, t);
            int terrainX = (int)MathF.Floor(point.X);
            int terrainZ = (int)MathF.Floor(point.Z);
            if (terrainX < 0 || terrainZ < 0 ||
                terrainX >= _world.Terrain.Width || terrainZ >= _world.Terrain.Height)
                continue;
            float terrainHeight = _world.Terrain.GetHeight(terrainX, terrainZ);
            if (point.Y > terrainHeight + 0.03f)
                continue;
            closestT = t;
            position = new Vector3(point.X, terrainHeight, point.Z);
            break;
        }

        foreach (Unit unit in _world.Units.Units)
        {
            if (unit.UnitId == attackerId || !unit.CanBeTargeted)
                continue;

            float footprintRadius = Math.Max(unit.Width, unit.Length) *
                _world.GameGrid.CellSize * 0.45f;
            float radius = Math.Max(0.35f, Math.Min(footprintRadius, unit.Height * 0.65f));
            Vector3 center = unit.Position + Vector3.Up * (unit.Height * 0.5f);
            if (!TrySegmentSphere(start, end, center, radius, out float t) || t >= closestT)
                continue;

            closestT = t;
            position = Vector3.Lerp(start, end, t);
            normal = position - center;
            normal = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.Up;
            hitUnitId = unit.UnitId;
        }

        return closestT != float.MaxValue;
    }

    private static bool TrySegmentSphere(
        Vector3 start,
        Vector3 end,
        Vector3 center,
        float radius,
        out float t)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            t = 0.0f;
            return Vector3.DistanceSquared(start, center) <= radius * radius;
        }

        t = MathHelper.Clamp(Vector3.Dot(center - start, segment) / lengthSquared, 0.0f, 1.0f);
        return Vector3.DistanceSquared(Vector3.Lerp(start, end, t), center) <= radius * radius;
    }

    private async Task ApplyExplosionDamageAsync(ProjectileImpact impact)
    {
        Unit[] targets = _world.Units.Units
            .Where(unit => unit.CanBeTargeted)
            .Where(unit =>
            {
                float x = unit.Position.X - impact.Position.X;
                float z = unit.Position.Z - impact.Position.Z;
                return x * x + z * z <= impact.ExplosionRadius * impact.ExplosionRadius;
            })
            .ToArray();

        foreach (Unit target in targets)
        {
            float horizontalDistance = Vector2.Distance(
                new Vector2(target.Position.X, target.Position.Z),
                new Vector2(impact.Position.X, impact.Position.Z));
            float damageFactor = MathHelper.Lerp(
                1.0f,
                0.25f,
                MathHelper.Clamp(horizontalDistance / impact.ExplosionRadius, 0.0f, 1.0f));
            float damage = DamageCalculator.Calculate(
                impact.Damage * damageFactor, impact.DamageType, target.Armor);
            damage = SquadBenefits.ApplyCombatModifiers(
                _world, _world.Units.FindById(impact.AttackerId), target, damage);
            HitInfo hit = new(impact.AttackerId, impact.Position, damage);
            bool destroyed = target.OnHit(hit);
            NetworkMessage hitCommand = NetworkCommands.CreateUnitHitCommand(
                _networkHandler.LocalPeerId,
                target.UnitId,
                impact.AttackerId,
                impact.Position,
                damage,
                target.HitPoints);
            _networkHandler.EnqueueLocalMessage(hitCommand);
            await _networkHandler.BroadcastAsync(hitCommand, CancellationToken.None);

            if (!destroyed)
                continue;

            _world.Units.Destroy(target.UnitId);
            NetworkMessage destroyCommand = NetworkCommands.CreateDestroyUnitCommand(
                _networkHandler.LocalPeerId,
                target.UnitId);
            _networkHandler.EnqueueLocalMessage(destroyCommand);
            await _networkHandler.BroadcastAsync(destroyCommand, CancellationToken.None);
        }
    }

    private Unit? FindImpactTarget(Guid attackerId, Vector3 impactPosition)
    {
        const float attackRadius = 1.5f;
        return _world.Units.Units.Where(unit =>
        {
            if (unit.UnitId == attackerId || !unit.CanBeTargeted) return false;
            float bottom = unit.Position.Y - (unit is Helicopter helicopter ? helicopter.GroundOffset : 0);
            if (impactPosition.Y < bottom - 0.25f || impactPosition.Y > bottom + unit.Height + 0.25f) return false;
            Vector2 offset = new(unit.Position.X - impactPosition.X, unit.Position.Z - impactPosition.Z);
            return offset.LengthSquared() <= attackRadius * attackRadius;
        }).OrderBy(unit => Vector3.DistanceSquared(unit.Position + Vector3.Up * unit.Height * 0.5f, impactPosition)).FirstOrDefault();
    }

    private async Task ApplyImpactDamageAsync(Guid attackerId, Vector3 impactPosition,
        float baseDamage, DamageType damageType)
    {
        Unit? target = FindImpactTarget(attackerId, impactPosition);

        if (target is null)
            return;

        float damage = DamageCalculator.Calculate(baseDamage, damageType, target.Armor);
        damage = SquadBenefits.ApplyCombatModifiers(
            _world, _world.Units.FindById(attackerId), target, damage);
        HitInfo hit = new(attackerId, impactPosition, damage);
        bool destroyed = target.OnHit(hit);
        NetworkMessage hitCommand = NetworkCommands.CreateUnitHitCommand(
            _networkHandler.LocalPeerId,
            target.UnitId,
            attackerId,
            impactPosition,
            damage,
            target.HitPoints);
        _networkHandler.EnqueueLocalMessage(hitCommand);
        await _networkHandler.BroadcastAsync(hitCommand, CancellationToken.None);

        if (!destroyed)
            return;

        _world.Units.Destroy(target.UnitId);
        NetworkMessage destroyCommand = NetworkCommands.CreateDestroyUnitCommand(
            _networkHandler.LocalPeerId,
            target.UnitId);
        _networkHandler.EnqueueLocalMessage(destroyCommand);
        await _networkHandler.BroadcastAsync(destroyCommand, CancellationToken.None);
    }
}
