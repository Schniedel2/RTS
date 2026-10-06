using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Diagnostics;

namespace RTS.Network;

public sealed class NetworkHost
{
    private static readonly Dictionary<NetworkMessageType, string> RequestMeasurementNames =
        Enum.GetValues<NetworkMessageType>().ToDictionary(type => type, type => "Host.Request." + type);
    private readonly NetworkHandler _networkHandler;
    private readonly GameWorld _world;
    private readonly EarthworkController _earthworks;
    private readonly Queue<NetworkMessage> _earthworkBroadcasts = new();
    private readonly ConcurrentQueue<NetworkMessage> _requestQueue = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly Dictionary<Guid, Vector2> _gotoQueueEnds = [];
    private readonly Dictionary<Guid, long> _planningVersions = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<NetworkMessage, Dictionary<Guid, long>> _requestVersions = new();
    private GotoPlan? _pendingGoto;
    private bool _pendingGotoReady;
    private int _pendingSchedulerGeneration;
    private sealed class GotoPlan(NetworkMessage request)
    {
        public NetworkMessage Request { get; } = request;
        public NetworkMessage ReceiptRequest = request;
        public NetworkMessage? Result;
        public Dictionary<Guid, long> Versions = [];
        public long GridRevision;
        public bool Invalidated;
    }
    private sealed class DestinationPlan
    {
        public bool Succeeded;
        public Vector2 Target;
        public Point[] Route = [];
    }
    private readonly HarvestSystem _harvest;
    private readonly Dictionary<Guid, int> _startPositionWishes = [];
    private readonly MedicSystem _medics;
    private const int MaximumQueuedRequests = 1024;
    private const int MaximumRequestsPerUpdate = 32;
    private const int MaximumGotoRequestsPerUpdate = 1;
    private const int MaximumStateUpdatesPerTick = 32;
    private const int MaximumGotoPathAttemptsPerUnit = 12;
    private const double HostSimulationInterval = 0.1;
    private const double StateHeartbeatInterval = 3.0;
    private long _sessionGeneration = -1;
    private int _groundStateCursor;
    private double _hostTime;
    private double _simulationAccumulator;
    private double _nextExploredVisibilitySync;
    /// <summary>Authoritative simulation time, shared with clients via NetworkHandler.EstimatedHostTime.</summary>
    public double HostTime => _hostTime;
    private readonly CombatSystem _combat;

    private readonly Func<CommandCenter, UnitActionType, bool> _commandCenterGoal;
    private readonly PricingService _pricing;
    private readonly ArmyHandler _armies;
    private readonly Func<IReadOnlyList<Player>> _players;
    private readonly Func<IReadOnlyCollection<AIPlayer>> _aiPlayers;
    private readonly Func<bool> _isMatchStarted;
    internal CombatSystem Combat => _combat;
    internal HarvestSystem Harvest => _harvest;
    internal MedicSystem Medics => _medics;
    internal EarthworkController Earthworks => _earthworks;

    public NetworkHost(
        NetworkHandler networkHandler,
        NetworkInput networkInput,
        GameWorld world) : this(networkHandler, networkInput, world, Globals.Game.Armies,
            () => Globals.Game.Players, () => Globals.Game.AIPlayers,
            (target, attacker) => Globals.Game.NotifyCombatLoss(target, attacker)) { }

    public NetworkHost(NetworkHandler networkHandler, NetworkInput networkInput, GameWorld world,
        ArmyHandler armies, Func<IReadOnlyList<Player>> players,
        Func<IReadOnlyCollection<AIPlayer>> aiPlayers, Action<Unit, Unit?> notifyCombatLoss, Func<bool>? isMatchStarted = null, Func<CommandCenter, UnitActionType, bool>? commandCenterGoal = null)
    {
        _armies = armies;
        _commandCenterGoal = commandCenterGoal ?? ((center, action) => Globals.Game.ApplyCommandCenterGoal(center, action));
        _isMatchStarted = isMatchStarted ?? (() => Globals.Game.IsMatchStarted);
        _players = players;
        _aiPlayers = aiPlayers;
        _networkHandler = networkHandler;
        _world = world;
        _pricing = new PricingService(armies, world.Units.FindById);
        _combat = new CombatSystem(world, networkHandler.LocalPeerId, PublishAsync, notifyCombatLoss);
        _harvest = new HarvestSystem(world, _armies, networkHandler.LocalPeerId,
            PublishAsync, QueueHarvestRoute, () => networkHandler.SessionGeneration,
            id => _planningVersions.GetValueOrDefault(id));
        _medics = new MedicSystem(world, _armies, networkHandler.LocalPeerId,
            PublishAsync, CreateStopCommand, QueueMedicRoute, () => networkHandler.SessionGeneration,
            id => _planningVersions.GetValueOrDefault(id));
        _earthworks = new EarthworkController(world, networkHandler.LocalPeerId, command =>
        {
            networkHandler.ApplyLocalCommand(command);
            _earthworkBroadcasts.Enqueue(command);
        }, refreshGraphics: world.GraphicsEnabled, armies: armies);
        networkInput.MessageReceived += HandleMessage;
        networkHandler.AIControllerAssignmentChanged += RejectSupersededAIRequests;
    }

    private void RejectSupersededAIRequests(AIControllerAssignment assignment)
    {
        bool Stale(NetworkMessage request) => request.AIControllerArmyId == assignment.ArmyId &&
            request.AIControllerGeneration != assignment.Generation;
        // Only pending admission/planning is removed. Accepted simulation jobs stay untouched.
        int count = _requestQueue.Count;
        for (int i = 0; i < count && _requestQueue.TryDequeue(out var request); i++)
        {
            if (!Stale(request)) { _requestQueue.Enqueue(request); continue; }
            _networkHandler.ResolveLocalRequest(request, false, "Controller assignment changed.");
            _requestVersions.Remove(request);
        }
        if (_pendingGoto is { } pending && Stale(pending.ReceiptRequest))
        {
            ReleasePlanningIntent(pending, supersededController: true);
            _networkHandler.ResolveLocalRequest(pending.ReceiptRequest, false, "Controller assignment changed during planning.");
            _pendingGoto = null; _pendingGotoReady = false;
        }
    }

    private async Task PublishEarthworkAsync()
    {
        // Keep patches, completion and replacement commands in order for every peer.
        while (_earthworkBroadcasts.TryDequeue(out NetworkMessage? command))
            await _networkHandler.BroadcastAsync(command, CancellationToken.None);
    }

    private async Task PublishHelicoptersAsync()
    {
        foreach (Helicopter helicopter in _world.Units.GetSnapshot().OfType<Helicopter>())
        {
            if (helicopter.HitPoints <= 0)
            {
                _world.Units.Destroy(helicopter.UnitId);
                var destroy = NetworkCommands.CreateDestroyUnitCommand(_networkHandler.LocalPeerId, helicopter.UnitId);
                _networkHandler.ApplyLocalCommand(destroy);
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
        if (!_networkHandler.IsHost) return;
        int sentUpdates = 0;
        MobileUnit[] units = _world.Units.Units.OfType<MobileUnit>().ToArray();
        int first = units.Length == 0 ? 0 : _groundStateCursor % units.Length;
        for (int visited = 0; visited < units.Length && sentUpdates < MaximumStateUpdatesPerTick; visited++)
        {
            int index = (first + visited) % units.Length;
            MobileUnit unit = units[index];
            _groundStateCursor = (index + 1) % units.Length;
            if (unit is Helicopter || unit.IsEmbarked || unit.IsDying || unit.IsLeavingBuilding ||
                _hostTime < unit.NextNetworkUpdateTime)
                continue;

            long navigationRevision = unit.NavigationRevision;
            bool includeNavigation = unit.HasUnpublishedNavigation || _hostTime >= unit.NextNavigationHeartbeat;
            UnitState state = unit.GetMovementState(includeNavigation);
            await _networkHandler.BroadcastAsync(
                NetworkCommands.CreateUnitStateCommand(_networkHandler.LocalPeerId, state));
            unit.MarkNavigationPublished(navigationRevision);
            if (includeNavigation) unit.NextNavigationHeartbeat = _hostTime + StateHeartbeatInterval;
            unit.NextNetworkUpdateTime = _hostTime + HostSimulationInterval;
            sentUpdates++;
        }
    }

    private NetworkMessage? TryCreateHelicopterOrder(NetworkMessage request)
    {
        if (request.UnitId is not Guid id || _world.Units.FindById(id) is not Helicopter helicopter ||
            !_armies.CanControl(request.SenderId, helicopter.ArmyId) ||
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
                if (request.SenderId != _networkHandler.LocalPeerId && !_armies.CanControl(request.SenderId, helicopter.ArmyId)) continue;
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
                 _armies.CanControl(request.SenderId, attacker.ArmyId)) &&
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
                 _armies.CanControl(request.SenderId, attacker.ArmyId)) &&
                attacker.CanAttackDomain(TargetDomain.Ground))
            .ToArray();
        return accepted.Length == 0
            ? null
            : NetworkCommands.CreateAttackGroundCommand(
                _networkHandler.LocalPeerId, request with { UnitIds = accepted });
    }

    internal void EnsureSessionGeneration()
    {
        long generation = _networkHandler.SessionGeneration;
        if (generation == _sessionGeneration) return;
        _sessionGeneration = generation;
        AbandonQueuedLocalRequests("Host session changed.");
        if (_pendingGoto is GotoPlan abandoned)
        {
            _networkHandler.AbandonLocalRequest(abandoned.ReceiptRequest, "Host session changed.");
            ReleasePlanningIntent(abandoned);
        }
        _pendingGoto = null;
        _pendingGotoReady = false;
        _planningVersions.Clear();
        _requestVersions.Clear();
        _world.PathfindingManager?.Reset();
        _earthworkBroadcasts.Clear();
        _earthworks.Reset();
        _gotoQueueEnds.Clear();
        _harvest.Reset();
        _startPositionWishes.Clear();
        _medics.Reset();
        _combat.Reset();
        _hostTime = 0;
        _reconSyncElapsed = 0;
        _simulationAccumulator = 0;
        _nextExploredVisibilitySync = 0;
        _groundStateCursor = 0;
        foreach (Unit unit in _world.Units.Units) unit.NextNetworkUpdateTime = 0;
    }

    private void AbandonQueuedLocalRequests(string reason)
    {
        while (_requestQueue.TryDequeue(out NetworkMessage? request))
            _networkHandler.AbandonLocalRequest(request, reason);
    }

    private void HandleMessage(NetworkMessage message)
    {
        if (message.Type.ToString().EndsWith("Request", StringComparison.Ordinal))
        {
            if (!AuthorizeController(message))
            { _networkHandler.ResolveLocalRequest(message, false, "Invalid or stale AI controller assignment."); return; }
            if (message.AIControllerActorId is Guid actor) message = message with { SenderId = actor, PlayerId = actor };
        }
        if (message.Type.ToString().EndsWith("Request", StringComparison.Ordinal) && _networkHandler.ReplayKnownRequest(message)) return;
        if (!AdmitRequest(message))
            _networkHandler.ResolveLocalRequest(message, false, "Host admission rejected the request (invalid payload, target or full inbox).");
    }

    internal bool AuthorizeController(NetworkMessage request)
    {
        bool ai = request.AIControllerArmyId.HasValue || request.AIControllerActorId.HasValue || request.AIControllerGeneration != 0;
        if (ai && !_networkHandler.AIControllers.Authorizes(request)) return false;
        if (ai && request.Type is NetworkMessageType.GrantArmyControlRequest or NetworkMessageType.RevokeArmyControlRequest or
            NetworkMessageType.TransferUnitRequest or NetworkMessageType.MergeArmiesRequest or NetworkMessageType.StartMultiplayerGameRequest or
            NetworkMessageType.SpawnRequest or NetworkMessageType.ToolActionRequest or NetworkMessageType.RequestPlayerUpdate) return false;
        if (ai && request.ConstructionSiteId is Guid siteId && _world.Units.FindById(siteId)?.ArmyId is Guid siteArmy && siteArmy != request.AIControllerArmyId) return false;
        if (!ai && _networkHandler.AIControllers.ForActor(request.SenderId) is not null) return false;
        Guid[] recipients = (request.UnitIds ?? []).Concat(request.UnitId is Guid singleId && request.Type != NetworkMessageType.BuildRequest ? new[] { singleId } : Array.Empty<Guid>()).Distinct().ToArray();
        foreach (Guid id in recipients)
        {
            Unit? unit = _world.Units.FindById(id);
            if (unit?.ArmyId is not Guid army) continue; // normal validation handles missing units
            if (ai && army != request.AIControllerArmyId) return false;
            if (!ai && _networkHandler.AIControllers.Find(army) is not null) return false;
        }
        if (request.ArmyId is Guid explicitArmy && (ai ? explicitArmy != request.AIControllerArmyId : _networkHandler.AIControllers.Find(explicitArmy) is not null)) return false;
        if (!ai && request.SecondaryArmyId is Guid secondary && _networkHandler.AIControllers.Find(secondary) is not null) return false;
        return true;
    }

    private bool AdmitRequest(NetworkMessage message)
    {
        if (!ComplexCommandPayloads.TryValidate(message, out _)) return false;
        EnsureSessionGeneration();
        if (!_networkHandler.IsHost)
            return false;

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
            message.Type != NetworkMessageType.SatelliteReconRequest &&
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
            return false;
        if (message.Type == NetworkMessageType.SpawnRequest && message.UnitTypeId is null)
            return false;
        if (message.Type is NetworkMessageType.GotoRequest or NetworkMessageType.MoveAwayRequest &&
            (!float.IsFinite(message.X) || !float.IsFinite(message.Z))) return false;
        if (message.Type == NetworkMessageType.GotoRequest &&
            !_world.GameGrid.Contains(_world.GameGrid.ToCell(new Vector3(message.X, 0, message.Z)))) return false;

        if (message.Type == NetworkMessageType.TextRequest && string.IsNullOrWhiteSpace(message.Text))
            return false;

        if (message.Type == NetworkMessageType.RequestPlayerUpdate &&
            (message.PlayerId is null || string.IsNullOrWhiteSpace(message.DisplayName)))
            return false;

        if (message.Type == NetworkMessageType.BuildConstructionRequest &&
            (message.ConstructionSiteId is null || message.UnitIds is null || message.UnitIds.Length == 0))
            return false;

        if (message.Type == NetworkMessageType.TrainUnitRequest &&
            (message.UnitId is null || string.IsNullOrWhiteSpace(message.UnitTypeId)))
            return false;
        if (message.Type == NetworkMessageType.ResearchRequest &&
            (message.UnitId is null || string.IsNullOrWhiteSpace(message.UnitTypeId)))
            return false;

        if (message.Type == NetworkMessageType.EnterUnitRequest &&
            (message.UnitId is null || message.TargetId is null))
            return false;

        if (message.Type == NetworkMessageType.LeaveContainerRequest && message.UnitId is null)
            return false;

        if (_requestQueue.Count >= MaximumQueuedRequests)
            return false;

        if (!_networkHandler.AdmitRequestId(message)) return true;
        RememberPlanningVersions(message);
        _requestQueue.Enqueue(message);
        return true;
    }

    public void Update(GameTime gameTime)
    {
        _networkHandler.AssertGameThread();
        EnsureSessionGeneration();
        if (!_networkHandler.IsHost) return;
        Task update = UpdateAsync(gameTime);
        if (!update.IsCompleted)
            throw new InvalidOperationException("Host simulation must never await network I/O.");
        update.GetAwaiter().GetResult();
    }

    public async Task UpdateAsync(GameTime gameTime)
    {
        _networkHandler.AssertGameThread();
        EnsureSessionGeneration();
        if (!_networkHandler.IsHost) return;
        if (!await _updateGate.WaitAsync(0))
            return;

        try
        {
            UpdateHostSimulation(gameTime);
            PublishRequestExecution();
            await PublishSatelliteReconAsync(gameTime);
            await PublishEarthworkAsync();
            await PublishHelicoptersAsync();
            await PublishGroundMobileUnitsAsync();
            await PublishExploredVisibilityAsync();
            await _harvest.UpdateAsync(gameTime, _hostTime);
            await _combat.PublishImpactsAsync();
            await _medics.UpdateAsync(_hostTime);

            if (_pendingGoto is GotoPlan finished)
            {
                if (_pendingSchedulerGeneration != _world.PathfindingManager.Scheduler.Generation)
                { _pendingGoto = null; _pendingGotoReady = true; finished.Result = null; }
                if (!_pendingGotoReady) return;
                using var publication = PerformanceMeasurements.Measure("Host.GotoPublish");
                _pendingGoto = null;
                _pendingGotoReady = false;
                NetworkMessage? planned = FilterPlannedCommand(finished);
                if (planned is not null) { planned.RequestId = finished.ReceiptRequest.RequestId; planned.RequestGeneration = finished.ReceiptRequest.RequestGeneration; CommitGotoEnds(planned); await PublishAsync(planned); }
                _networkHandler.ResolveLocalRequest(finished.ReceiptRequest, planned is not null,
                    planned is not null ? null : finished.Versions.Keys.Any(id =>
                        _world.Units.FindMobileUnitById(id) is MobileUnit mobile && PlanUnitValid(finished, mobile))
                        ? "No reachable route survived host planning." : "The movement order was superseded or its units disappeared.");
                ReleasePlanningIntent(finished);
            }

            foreach (NetworkMessage queuedRequest in TakeRequestsForUpdate())
            {
                using var measurement = PerformanceMeasurements.Measure(RequestMeasurementNames[queuedRequest.Type]);
                if (!AuthorizeController(queuedRequest)) { _networkHandler.ResolveLocalRequest(queuedRequest, false, "Controller assignment changed."); continue; }
                NetworkMessage request = queuedRequest;

                if (request.Type is NetworkMessageType.GotoRequest or NetworkMessageType.StopRequest or
                    NetworkMessageType.FollowRequest or NetworkMessageType.AttackTargetRequest or NetworkMessageType.AttackGroundRequest)
                    request = request with { UnitIds = (request.UnitIds ?? Array.Empty<Guid>()).Where(id =>
                        _world.Units.FindById(id) is not Helicopter helicopter || _armies.CanControl(request.SenderId, helicopter.ArmyId)).ToArray() };
                _medics.HandleCommandOverride(request, ExpandSquadUnitIds(request.UnitIds ?? []));
                _earthworks.CancelForRequest(request);
                _harvest.CancelForRequest(request);
                if (request.Type is NetworkMessageType.GotoRequest or NetworkMessageType.MoveAwayRequest)
                {
                    GotoPlan planning = NewGotoPlan(request);
                    planning.ReceiptRequest = queuedRequest;
                    // Filtering helicopter IDs above creates a record copy. Keep
                    // the original admission versions, including newer queued Stop/Goto.
                    if (_requestVersions.TryGetValue(queuedRequest, out Dictionary<Guid, long>? admittedVersions))
                        planning.Versions = admittedVersions;
                    _pendingGoto = planning;
                    _pendingSchedulerGeneration = _world.PathfindingManager.Scheduler.Generation;
                    _pendingGotoReady = false;
                    if (!request.AppendToQueue)
                    {
                        await PublishAsync(CreateStopCommand(planning.Request with {
                            UnitIds = planning.Versions.Keys.Where(id =>
                                _world.Units.FindMobileUnitById(id) is MobileUnit mobile && PlanUnitValid(planning, mobile)).ToArray() }));
                        foreach (Guid id in planning.Versions.Keys)
                            if (_world.Units.FindMobileUnitById(id) is MobileUnit mobile && PlanUnitValid(planning, mobile))
                                mobile.MarkHostPlanning(new Vector2(request.X, request.Z));
                    }
                    long generation = _sessionGeneration;
                    _world.PathfindingManager.Scheduler.Enqueue(
                        request.Type == NetworkMessageType.MoveAwayRequest ? PlanMoveAway(planning) : PlanGoto(planning),
                        () => generation == _networkHandler.SessionGeneration && ReferenceEquals(_pendingGoto, planning),
                        () => _pendingGotoReady = true, () => _pendingGotoReady = true, "Host.GotoPlanningSlice");
                    break; // A later request cannot overtake this asynchronous FIFO head.
                }
                NetworkMessage? command = request.Type switch
                {
                    NetworkMessageType.SpawnRequest => NetworkCommands.CreateSpawnCommand(_networkHandler.LocalPeerId, request),
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
                    NetworkMessageType.BuildConstructionRequest => TryCreateConstructionCommand(request),
                    NetworkMessageType.TrainUnitRequest => TryCreateTrainUnitCommand(request),
                    NetworkMessageType.SatelliteReconRequest => TryCreateSatelliteReconCommand(request),
                    NetworkMessageType.ResearchRequest => TryCreateResearchCommand(request),
                    NetworkMessageType.SetRallyPointRequest => TryCreateSetRallyPointCommand(request),
                    NetworkMessageType.EarthworkRequest => _earthworks.Start(request),
                    NetworkMessageType.HelicopterOrderRequest => TryCreateHelicopterOrder(request),
                    NetworkMessageType.HarvestRequest => _harvest.TryStart(request),
                    NetworkMessageType.HarvesterReturnRequest => _harvest.TryReturn(request),
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
                {
                    AIOrderFailure failure = ExplainRejection(queuedRequest);
                    _networkHandler.ResolveLocalRequest(queuedRequest, false,
                        $"Host rejected {queuedRequest.Type}: {failure}.", failure);
                    continue;
                }

                command.RequestId = queuedRequest.RequestId;
                command.RequestGeneration = queuedRequest.RequestGeneration;
                _networkHandler.ApplyLocalCommand(command);
                await _networkHandler.BroadcastAsync(command, CancellationToken.None);
                _networkHandler.ResolveLocalRequest(queuedRequest, true);

                if (request.Type == NetworkMessageType.AttackRequest)
                    await _combat.ResolveAttackAsync(request with { UnitIds = command.UnitIds });
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private void PublishRequestExecution()
    {
        foreach (NetworkMessage request in _networkHandler.AcceptedRequests)
        {
            Guid? siteId = request.Type == NetworkMessageType.BuildRequest ? request.UnitId : request.ConstructionSiteId;
            if (request.Type is NetworkMessageType.BuildRequest or NetworkMessageType.BuildConstructionRequest && siteId is Guid site)
            {
                if (_world.Units.FindById(site) is not Building building || building.IsDying)
                    _networkHandler.ReportRequestExecution(request, AIOrderStatus.Failed, AIOrderFailure.InvalidTarget, "Construction lost.");
                else _networkHandler.ReportRequestExecution(request, building.IsCompleted ? AIOrderStatus.Completed : AIOrderStatus.InProgress);
            }
            else if (request.Type is NetworkMessageType.TrainUnitRequest or NetworkMessageType.ResearchRequest && request.ProductionOrderId is Guid order)
            {
                if (_world.Units.FindById(request.UnitId!.Value) is not Building producer || producer.IsDying)
                    _networkHandler.ReportRequestExecution(request, AIOrderStatus.Failed, AIOrderFailure.Producer, "Producer lost.");
                else if (producer.ProductionQueue.WasCompleted(order))
                    _networkHandler.ReportRequestExecution(request, AIOrderStatus.Completed);
                else if (producer.ProductionQueue.Orders.Any(item => item.OrderId == order))
                    _networkHandler.ReportRequestExecution(request, AIOrderStatus.InProgress);
                else _networkHandler.ReportRequestExecution(request, AIOrderStatus.Failed, AIOrderFailure.Cancelled, "Production cancelled.");
            }
            else _networkHandler.ReportRequestExecution(request, AIOrderStatus.Completed);
        }
    }

    // This game-thread consumer is the only reader removing requests. Deferred
    // work stays at the head so Stop, Attack and Shift orders cannot overtake it.
    private IEnumerable<NetworkMessage> TakeRequestsForUpdate()
    {
        int gotoRequests = 0;
        int available = Math.Min(MaximumRequestsPerUpdate, _requestQueue.Count);
        for (int index = 0; index < available; index++)
        {
            if (!_requestQueue.TryPeek(out NetworkMessage? next)) yield break;
            if (next.Type == NetworkMessageType.GotoRequest &&
                gotoRequests >= MaximumGotoRequestsPerUpdate)
                yield break;
            if (!_requestQueue.TryDequeue(out NetworkMessage? request)) yield break;
            if (request.Type == NetworkMessageType.GotoRequest) gotoRequests++;
            yield return request;
        }
    }

    private float _reconSyncElapsed;
    internal NetworkMessage? TryCreateSatelliteReconCommand(NetworkMessage request)
    {
        if (request.ArmyId is not Guid id || !_armies.CanControl(request.SenderId, id) ||
            _armies.Find(id) is not Army army || !SatelliteRecon.Ready(_world, army)) return null;
        return new(NetworkMessageType.SatelliteReconCommand, _networkHandler.LocalPeerId,
            ArmyId: id, SatelliteRecon: new(SatelliteRecon.CooldownSeconds, SatelliteRecon.ScanSeconds));
    }

    private async Task PublishSatelliteReconAsync(GameTime gameTime)
    {
        float seconds = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _reconSyncElapsed += seconds;
        bool sync = _reconSyncElapsed >= 1;
        if (sync) _reconSyncElapsed %= 1;
        foreach (Army army in _armies.Armies)
        {
            SatelliteReconState previous = army.SatelliteRecon;
            if (previous.Cooldown <= 0 && previous.ActiveSeconds <= 0) continue;
            army.SatelliteRecon = SatelliteRecon.Advance(_world, army, seconds);
            bool ended = previous.ActiveSeconds > 0 && army.SatelliteRecon.ActiveSeconds == 0;
            if (ended) _world.Visibility.Update();
            if (sync || ended || (previous.Cooldown > 0 && army.SatelliteRecon.Cooldown == 0))
                await PublishAsync(new(NetworkMessageType.SatelliteReconCommand, _networkHandler.LocalPeerId,
                    ArmyId: army.Id, SatelliteRecon: army.SatelliteRecon));
        }
    }

    private Task PublishExploredVisibilityAsync()
    {
        if (_hostTime < _nextExploredVisibilitySync) return Task.CompletedTask;
        _nextExploredVisibilitySync = _hostTime + 1.0;
        return _networkHandler.BroadcastAsync(new NetworkMessage(
            NetworkMessageType.ExploredVisibilityCommand, _networkHandler.LocalPeerId,
            ExploredVisibility: _world.Visibility.GetExploredSnapshots()));
    }

    private NetworkMessage CreateTransferUnitCommand(NetworkMessage request)
    {
        Player? recipient = request.TargetId is Guid playerId
            ? _players().FirstOrDefault(player => player.Id == playerId)
            : null;
        return NetworkCommands.CreateTransferUnitCommand(
            _networkHandler.LocalPeerId, request, recipient?.ArmyId ?? Guid.Empty);
    }

    private NetworkMessage? TryCreateGotoCommand(NetworkMessage request)
    {
        using var measurement = PerformanceMeasurements.Measure("Host.GotoPlanning");
        long started = Stopwatch.GetTimestamp();
        try
        {
            return TryCreateGotoCommandCore(request);
        }
        finally
        {
            double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Globals.Telemetry.HostGoto_Calls++;
            Globals.Telemetry.HostGoto_Last = elapsed;
            Globals.Telemetry.HostGoto_Max = Math.Max(Globals.Telemetry.HostGoto_Max, elapsed);
        }
    }

    private NetworkMessage? TryCreateGotoCommandCore(NetworkMessage request)
    {
        // Synchronous compatibility entry for the graphics-free checks. Runtime
        // consumers enqueue PlanGoto into the shared navigation scheduler.
        GotoPlan plan = NewGotoPlan(request);
        foreach (int step in PlanGoto(plan)) { }
        if (plan.Result is not null) CommitGotoEnds(plan.Result);
        return plan.Result;
    }

    private void CommitGotoEnds(NetworkMessage command)
    {
        foreach (UnitRoute route in command.Routes ?? [])
            if (route.TargetX is float x && route.TargetZ is float z) _gotoQueueEnds[route.UnitId] = new(x, z);
    }

    private NetworkMessage? FilterPlannedCommand(GotoPlan plan)
    {
        if (plan.Result is not NetworkMessage command || plan.GridRevision != _world.GameGrid.NavigationRevision) return null;
        UnitRoute[] routes = (command.Routes ?? []).Where(route =>
            _world.Units.FindMobileUnitById(route.UnitId) is MobileUnit unit && PlanUnitValid(plan, unit)).ToArray();
        return routes.Length == 0 ? null : command with { Routes = routes, UnitIds = routes.Select(route => route.UnitId).ToArray() };
    }

    private GotoPlan NewGotoPlan(NetworkMessage request)
    {
        GotoPlan plan = new(request);
        if (_requestVersions.TryGetValue(request, out Dictionary<Guid, long>? versions)) plan.Versions = versions;
        else foreach (Guid id in ExpandSquadUnitIds(request.UnitIds ?? (request.UnitId is Guid single ? [single] : [])))
            plan.Versions[id] = _planningVersions.GetValueOrDefault(id);
        return plan;
    }

    private bool PlanVersionValid(GotoPlan plan, MobileUnit unit) =>
        !unit.IsDying && !unit.IsEmbarked &&
        AuthorizeController(plan.Request) && _armies.CanControl(plan.Request.SenderId, unit.ArmyId) &&
        plan.Versions.TryGetValue(unit.UnitId, out long version) && version == _planningVersions.GetValueOrDefault(unit.UnitId);

    private bool PlanUnitValid(GotoPlan plan, MobileUnit unit) =>
        PlanVersionValid(plan, unit) && ReferenceEquals(_world.Units.FindById(unit.UnitId), unit);

    private void RememberPlanningVersions(NetworkMessage request)
    {
        bool replaces = !request.AppendToQueue && request.Type is NetworkMessageType.GotoRequest or
            NetworkMessageType.StopRequest or NetworkMessageType.MoveAwayRequest or
            NetworkMessageType.BuildConstructionRequest or NetworkMessageType.EnterUnitRequest or
            NetworkMessageType.HarvesterReturnRequest or NetworkMessageType.HarvestRequest ||
            request.Type == NetworkMessageType.UnitActionRequest && request.UnitActionType == UnitActionType.Stop;
        Dictionary<Guid, long> versions = [];
        IEnumerable<Guid> ids = ExpandSquadUnitIds(request.UnitIds ?? (request.UnitId is Guid id ? [id] : []));
        foreach (Guid unitId in ids.Distinct())
        {
            if (_world.Units.FindMobileUnitById(unitId) is not MobileUnit unit ||
                !_armies.CanControl(request.SenderId, unit.ArmyId)) continue;
            long version = _planningVersions.GetValueOrDefault(unitId);
            if (replaces) _planningVersions[unitId] = ++version;
            versions[unitId] = version;
        }
        _requestVersions.Remove(request);
        _requestVersions.Add(request, versions);
    }

    private IEnumerable<int> PlanGoto(GotoPlan plan)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            plan.Invalidated = false;
            plan.Result = null;
            foreach (int step in PlanGotoAttempt(plan)) yield return step;
            if (!plan.Invalidated) yield break;
        }
    }

    private IEnumerable<int> PlanGotoAttempt(GotoPlan plan)
    {
        NetworkMessage request = plan.Request;
        yield return 0;
        Vector2 target = new(request.X, request.Z);
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y)) yield break;
        Point destination = _world.GameGrid.ToCell(new Vector3(target.X, 0, target.Y));
        if (!_world.GameGrid.Contains(destination)) yield break;
        List<UnitRoute> routes = [];
        Guid[] requestedIds = request.UnitIds ?? [];
        Dictionary<Guid, Vector2> formationTargets = [];
        foreach (SquadLeader leader in requestedIds.Distinct()
            .Select(_world.Units.FindById)
            .OfType<SquadLeader>()
            .Where(leader => _armies.CanControl(request.SenderId, leader.ArmyId)))
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
            .Where(unit => unit is not null && _armies.CanControl(request.SenderId, unit.ArmyId))
            .Cast<MobileUnit>()
            .OrderBy(unit => Vector2.DistanceSquared(new Vector2(unit.Position.X, unit.Position.Z), target))
            .ToArray();
        HashSet<Point> reservedDestinations = [];
        bool distributeGroup = units.Length > 1;

        foreach (MobileUnit unit in units)
        {
            yield return 0;
            if (!PlanUnitValid(plan, unit)) continue;
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
            else
            {
                DestinationPlan assigned = new();
                foreach (int step in AssignGotoDestination(unit, requestedTarget, requestedDestination, reservedDestinations,
                    request.AppendToQueue, distributeGroup, assigned))
                {
                    yield return step;
                    if (!PlanVersionValid(plan, unit)) break;
                }
                if (!PlanUnitValid(plan, unit)) continue;
                assignedTarget = assigned.Target;
                proposed = assigned.Route;
                if (!assigned.Succeeded)
                {
                // No useful position is reachable near the group destination.
                // Sending an empty route cancels an older movement order and
                // leaves the unit standing instead of running against a blocker.
                assignedTarget = new Vector2(unit.Position.X, unit.Position.Z);
                proposed = [];
                }
            }

            routes.Add(new UnitRoute(id, proposed!, assignedTarget.X, assignedTarget.Y));
        }
        // An unrelated building may have appeared while later units were planned.
        // Revalidate every route before publishing the complete group command.
        List<UnitRoute> accepted = [];
        long validationRevision = _world.GameGrid.NavigationRevision;
        foreach (UnitRoute route in routes)
        {
            MobileUnit? unit = _world.Units.FindMobileUnitById(route.UnitId);
            if (unit is null || !PlanUnitValid(plan, unit)) continue;
            bool valid = true;
            Point from = request.AppendToQueue && _gotoQueueEnds.TryGetValue(unit.UnitId, out Vector2 queuedEnd)
                ? _world.GameGrid.ToCell(new Vector3(queuedEnd.X, 0, queuedEnd.Y)) : _world.GameGrid.ToCell(unit.Position);
            HashSet<Point> startingFootprint = _world.GameGrid.GetPathfindingStartingFootprint(unit, from);
            bool CanUseCell(Point cell) => unit.MovementProfile.CanEnter(_world, unit, cell) &&
                _world.GameGrid.IsPathfindingAllowedFromFootprint(unit, cell, startingFootprint);
            foreach (Point cell in route.Cells)
            {
                yield return 0;
                if (_world.GameGrid.NavigationRevision != validationRevision) { plan.Invalidated = true; yield break; }
                // Client-supplied routes retain the existing minimal validation;
                // collision checks during driving handle changes along those routes.
                if (!distributeGroup && formationTargets.Count == 0 && request.Routes?.Any(supplied => supplied.UnitId == unit.UnitId) == true)
                { from = cell; continue; }
                if (Math.Abs(cell.X - from.X) > 1 || Math.Abs(cell.Y - from.Y) > 1 || !CanUseCell(cell) ||
                    (cell.X != from.X && cell.Y != from.Y &&
                        (!CanUseCell(new Point(cell.X, from.Y)) || !CanUseCell(new Point(from.X, cell.Y)))))
                { valid = false; break; }
                from = cell;
            }
            if (!PlanUnitValid(plan, unit)) continue;
            accepted.Add(valid ? route : new UnitRoute(unit.UnitId, [], unit.Position.X, unit.Position.Z));
        }
        if (_world.GameGrid.NavigationRevision != validationRevision) { plan.Invalidated = true; yield break; }
        plan.Result = accepted.Count == 0 ? null : NetworkCommands.CreateGotoCommand(_networkHandler.LocalPeerId,
            request with { UnitIds = accepted.Select(route => route.UnitId).ToArray() }, accepted.ToArray());
        plan.GridRevision = _world.GameGrid.NavigationRevision;
    }

    private IEnumerable<int> AssignGotoDestination(
        MobileUnit unit,
        Vector2 requestedTarget,
        Point center,
        HashSet<Point> reservedDestinations,
        bool appendToQueue,
        bool distributeGroup,
        DestinationPlan result)
    {
        Vector2 startPosition = appendToQueue && _gotoQueueEnds.TryGetValue(unit.UnitId, out Vector2 queuedEnd)
            ? queuedEnd
            : new Vector2(unit.Position.X, unit.Position.Z);
        Point start = _world.GameGrid.ToCell(new Vector3(startPosition.X, 0, startPosition.Y));
        int maximumRadius = distributeGroup ? Math.Max(6, (int)MathF.Ceiling(MathF.Sqrt(reservedDestinations.Count + 1)) + 3) : 0;
        int pathAttempts = 0;

        for (int radius = 0; radius <= maximumRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    yield return 0;
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
                    if (pathAttempts++ >= MaximumGotoPathAttemptsPerUnit)
                    {
                        yield break;
                    }
                    Pathfinder.Search search = _world.PathfindingManager.CreateSearch(unit, start, candidateTarget);
                    foreach (int step in search.Work()) yield return step;
                    if (!search.Succeeded)
                        continue;

                    reservedDestinations.Add(candidate);
                    result.Target = candidateTarget;
                    result.Route = search.Path.ToArray();
                    result.Succeeded = true;
                    yield break;
                }
            }
        }

        yield break;
    }

    private NetworkMessage CreateStopCommand(NetworkMessage request)
    {
        Guid[] ids = ExpandSquadUnitIds(request.UnitIds ?? [])
            .Where(id => _world.Units.FindById(id) is Unit unit &&
                _armies.CanControl(request.SenderId, unit.ArmyId))
            .ToArray();
        foreach (Guid id in ids)
        {
            _gotoQueueEnds.Remove(id);
            _harvest.Cancel(id);
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
            _armies.CanControl(request.SenderId, unit.ArmyId)).ToArray();
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
                     (unit.SquadLeaderId is null ||
                      _world.Units.FindById(unit.SquadLeaderId.Value) is not SquadLeader previousLeader ||
                      previousLeader.IsDying || previousLeader.ArmyId != armyId) &&
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
            if (commandCenter is null || !_commandCenterGoal(commandCenter, actionType))
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

    private NetworkMessage? TryCreateMoveAwayCommand(NetworkMessage request)
    {
        GotoPlan plan = NewGotoPlan(request);
        foreach (int step in PlanMoveAway(plan)) { }
        if (plan.Result is not null) CommitGotoEnds(plan.Result);
        return plan.Result;
    }

    private IEnumerable<int> PlanMoveAway(GotoPlan plan)
    {
        NetworkMessage request = plan.Request;
        if (request.UnitId is not Guid id || _world.Units.FindMobileUnitById(id) is not MobileUnit unit ||
            unit.IsDying || !_armies.CanControl(request.SenderId, unit.ArmyId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Z)) yield break;

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
                yield return 0;
                if (!PlanUnitValid(plan, unit)) yield break;
                Vector2 direction = Vector2.Transform(away, Matrix.CreateRotationZ(MathHelper.ToRadians(offset)));
                Vector2 destination = new Vector2(unit.Position.X, unit.Position.Z) + direction * distanceInCells * cellSize;
                NetworkMessage candidate = NetworkCommands.CreateGotoRequest(request.SenderId, [id],
                    destination.X, unit.Position.Y, destination.Y);
                GotoPlan trial = new(candidate) { Versions = plan.Versions };
                foreach (int step in PlanGoto(trial)) yield return step;
                if (trial.Result?.Routes?.Any(route => route.Cells.Length > 0) == true)
                { plan.Result = trial.Result; plan.GridRevision = trial.GridRevision; yield break; }
            }
        }
        yield break;
    }

    private void QueueHarvestRoute(Harvester harvester,
        IEnumerable<(Point? Resource, Vector3 Position)> candidates, Func<bool> valid,
        Action<Point?, NetworkMessage?> completed, Action cancelled)
    {
        GotoPlan? chosen = null;
        Point? chosenResource = null;
        IEnumerable<int> Work()
        {
            Guid sender = harvester.ArmyId is Guid armyId && _armies.Find(armyId) is Army army
                ? army.OwnerPlayerIds.FirstOrDefault() : _networkHandler.LocalPeerId;
            foreach (var candidate in candidates)
            {
                yield return 0;
                if (!valid()) yield break;
                GotoPlan trial = NewGotoPlan(NetworkCommands.CreateGotoRequest(sender, [harvester.UnitId],
                    candidate.Position.X, candidate.Position.Y, candidate.Position.Z));
                foreach (int step in PlanGoto(trial)) yield return step;
                if (trial.Result?.Routes?.Any(route => route.Cells.Length > 0) != true) continue;
                chosen = trial;
                chosenResource = candidate.Resource;
                yield break;
            }
        }
        _world.PathfindingManager.Scheduler.Enqueue(Work(), valid, () =>
        {
            NetworkMessage? result = chosen is null ? null : FilterPlannedCommand(chosen);
            if (result is not null) CommitGotoEnds(result);
            completed(chosenResource, result);
        }, cancelled);
    }

    private void QueueMedicRoute(Medic medic, Vector3 target, Func<bool> valid,
        Action<NetworkMessage?> completed, Action cancelled)
    {
        Guid sender = medic.ArmyId is Guid armyId && _armies.Find(armyId) is Army army &&
            army.OwnerPlayerIds.Count > 0 ? army.OwnerPlayerIds.OrderBy(id => id).First() : _networkHandler.LocalPeerId;
        GotoPlan plan = NewGotoPlan(NetworkCommands.CreateGotoRequest(sender, [medic.UnitId], target.X, target.Y, target.Z));
        _world.PathfindingManager.Scheduler.Enqueue(PlanGoto(plan),
            () => valid() && PlanUnitValid(plan, medic), () =>
            {
                NetworkMessage? move = FilterPlannedCommand(plan);
                if (move is not null) CommitGotoEnds(move);
                completed(move);
            }, cancelled);
    }

    private void ReleasePlanningIntent(GotoPlan plan, bool supersededController = false)
    {
        foreach (Guid id in plan.Versions.Keys)
            if (_world.Units.FindMobileUnitById(id) is MobileUnit mobile &&
                (PlanUnitValid(plan, mobile) || supersededController && _planningVersions.GetValueOrDefault(id) == plan.Versions[id]) &&
                mobile.MovementStatus == MovementStatus.Planning &&
                mobile.CurrentCommand?.Target == new Vector2(plan.Request.X, plan.Request.Z))
                mobile.ClearCommand();
    }

    private async Task PublishAsync(NetworkMessage command)
    {
        _networkHandler.ApplyLocalCommand(command);
        await _networkHandler.BroadcastAsync(command, CancellationToken.None);
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.X - second.X, z = first.Z - second.Z;
        return x * x + z * z;
    }

    private NetworkMessage? TryCreateConstructionCommand(NetworkMessage request)
    {
        if (request.ConstructionSiteId is not Guid id || _world.Units.FindById(id) is not Building site ||
            site.IsDying || site.IsCompleted || !_armies.CanControl(request.SenderId, site.ArmyId)) return null;
        Guid[] workers = (request.UnitIds ?? []).Where(workerId =>
            _world.Units.FindById(workerId) is MobileUnit worker && !worker.IsDying && !worker.IsEmbarked &&
            worker.BuildRate > 0 && _armies.CanControl(request.SenderId, worker.ArmyId) && worker.ArmyId == site.ArmyId)
            .Distinct().ToArray();
        return workers.Length == 0 ? null : NetworkCommands.CreateBuildConstructionCommand(
            _networkHandler.LocalPeerId, request with { UnitIds = workers });
    }

    private AIOrderFailure ExplainRejection(NetworkMessage request)
    {
        if (request.Type is NetworkMessageType.BuildRequest or NetworkMessageType.TrainUnitRequest or NetworkMessageType.ResearchRequest)
        {
            bool build = request.Type == NetworkMessageType.BuildRequest;
            Building? producer = request.UnitId is Guid id ? _world.Units.FindById(id) as Building : null;
            if (!build && (producer is null || producer.IsDying || !producer.IsCompleted ||
                !_armies.CanControl(request.SenderId, producer.ArmyId))) return AIOrderFailure.Producer;
            Guid? armyId = build ? _players().FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId : producer?.ArmyId;
            if (armyId is not Guid owner || _armies.Find(owner) is not Army army) return AIOrderFailure.InvalidTarget;
            PurchasableType type = build ? PurchasableType.Building : request.Type == NetworkMessageType.ResearchRequest
                ? PurchasableType.Research : PurchasableType.Unit;
            PurchaseQuote quote = _pricing.GetQuote(new(type, request.UnitTypeId ?? "", owner,
                build ? null : producer?.UnitId));
            if (quote.MissingPerks.Count > 0) return AIOrderFailure.Perk;
            if (!quote.IsAvailable) return AIOrderFailure.Validation;
            if (!quote.CanAfford(army.Resources)) return AIOrderFailure.Resources;
            if (type == PurchasableType.Research && ResearchProjects.TryGetGrantedPerk(request.UnitTypeId ?? "", out var perk)
                && army.Perks.Has(perk)) return AIOrderFailure.Perk;
            if (!build) return AIOrderFailure.Producer;
            if (request.UnitId is Guid site && _world.Units.FindById(site) is not null) return AIOrderFailure.InvalidTarget;
            return AIOrderFailure.BuildSite;
        }
        return AIOrderFailure.InvalidTarget;
    }

    private NetworkMessage? TryCreateBuildCommand(NetworkMessage request)
    {
        if (!ComplexCommandPayloads.TryValidate(request, out _)) return null;
        if (string.IsNullOrWhiteSpace(request.UnitTypeId) ||
            !float.IsFinite(request.X) || !float.IsFinite(request.Y) || !float.IsFinite(request.Z) ||
            !float.IsFinite(request.TargetAngleY) || request.X < 0 || request.Z < 0 ||
            request.X >= _world.Terrain.Width - 1 || request.Z >= _world.Terrain.Height - 1)
            return null;
        Player? player = _players().FirstOrDefault(player => player.Id == request.SenderId);
        if (player is null || _armies.Find(player.ArmyId) is not Army army)
            return null;
        Guid armyId = player.ArmyId;
        PurchaseQuote quote = _pricing.GetQuote(new PurchaseRequest(
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
            request.TargetAngleY, unitId, request.SenderId, quote.FinalPrice, armyId);
        if (building is null)
            return null;
        ArmyResourceService.TrySpend(army, _world, building.PurchasePrice);
        return NetworkCommands.CreateBuildCommand(_networkHandler.LocalPeerId,
            request with { UnitId = unitId, PlayerId = request.SenderId, Y = position.Y,
                UnitIds = (request.UnitIds ?? Array.Empty<Guid>()).Distinct().Where(id =>
                    _world.Units.FindById(id) is MobileUnit worker && worker.BuildRate > 0 &&
                    worker.IsSelectable && _armies.CanControl(request.SenderId, worker.ArmyId)).ToArray(),
                ArmyId = armyId, ResourceAmount = army.Resources, PurchasePrice = quote.FinalPrice });
    }

    private NetworkMessage? TryCreateSellBuildingCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            _world.Units.FindById(buildingId) is not Building building ||
            building is GenericBuilding || building.IsDying || !building.IsCompleted ||
            building.ArmyId is not Guid armyId ||
            _players().FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId != armyId ||
            building.Occupancy?.Occupants.Count > 0 ||
            _armies.Find(armyId) is not Army army)
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
            _players().FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId != armyId ||
            _armies.Find(armyId) is not Army army)
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
            _players().FirstOrDefault(player => player.Id == request.SenderId)?.ArmyId != armyId)
        {
            return null;
        }

        return NetworkCommands.CreateDestroyUnitCommand(_networkHandler.LocalPeerId, buildingId);
    }

    private NetworkMessage? RememberStartPositionWish(NetworkMessage request)
    {
        if (request.StartPositionSlot is not int slot ||
            !_players().Any(player => player.Id == request.SenderId) ||
            !_world.GameplayMarkers.Markers.Any(marker =>
                marker.Type == GameplayMarkerType.PlayerStart && marker.PlayerSlot == slot))
        {
            return null;
        }

        _startPositionWishes[request.SenderId] = slot;
        Globals.Console?.Print($"Player {request.SenderId.ToString("N")[..8]} requested start position {slot}.");
        return null;
    }

    internal NetworkMessage? TryCreateStartMultiplayerGameCommand(NetworkMessage request)
    {
        if (request.SenderId != _networkHandler.LocalPeerId)
            return null;

        // Validate before publishing/resetting the world or starting any controller.
        try { _ = AIProfileCatalog.Default; _ = AIRuntimeSettings.Default; _ = AIRuntimeSettings.LocalCompute; }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Globals.Console?.Print($"Cannot start game: {error.Message}");
            return null;
        }

        // AI controllers live only on the host. Include their player identities
        // explicitly so a map publish or player-list rebuild cannot drop them
        // from the following match.
        Player[] players = _players()
            .Concat(_aiPlayers().Select(ai => ai.Player))
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
                _aiPlayers().Any(ai => ai.Id == player.Id), Guid.NewGuid());
        }).ToArray();
        _startPositionWishes.Clear();
        if (_pendingGoto is GotoPlan abandoned)
        {
            _networkHandler.AbandonLocalRequest(abandoned.ReceiptRequest, "Match restarted.");
            ReleasePlanningIntent(abandoned);
        }
        _pendingGoto = null;
        _pendingGotoReady = false;
        AbandonQueuedLocalRequests("Match restarted.");
        _planningVersions.Clear();
        _requestVersions.Clear();
        _gotoQueueEnds.Clear();
        _world.PathfindingManager.Reset();
        _earthworks.Reset();
        _earthworkBroadcasts.Clear();
        _harvest.Reset();
        _medics.Reset();
        _combat.Reset();
        _nextExploredVisibilitySync = 0;
        return NetworkCommands.CreateStartMultiplayerGameCommand(
            _networkHandler.LocalPeerId, assignments);
    }

    private NetworkMessage? TryCreateSetRallyPointCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid unitId || request.RallyPoint is not RallyPointState requested ||
            _world.Units.FindById(unitId) is not Unit unit || !unit.SupportsRallyPoint || unit.IsDying ||
            !_armies.CanControl(request.SenderId, unit.ArmyId))
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
            !_armies.CanControl(request.SenderId, building.ArmyId) ||
            !building.TryGetProductionDuration(request.UnitTypeId, out float durationSeconds) ||
            building.ArmyId is not Guid armyId ||
            _armies.Find(armyId) is not Army army)
        {
            return null;
        }

        if (!building.CanProduceUnit(_world, request.UnitTypeId)) return null;

        PurchaseQuote quote = _pricing.GetQuote(new PurchaseRequest(
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
            _world.Units.FindById(buildingId) is not Building building ||
            !building.IsCompleted ||
            !ResearchProjects.TryGetGrantedPerk(request.UnitTypeId, out PerkType perk) ||
            !_armies.CanControl(request.SenderId, building.ArmyId) ||
            !building.TryGetProductionDuration(request.UnitTypeId, out float durationSeconds) ||
            building.ArmyId is not Guid armyId ||
            _armies.Find(armyId) is not Army army ||
            army.Perks.Has(perk) ||
            _world.Units.Units.OfType<Building>().Any(candidate => candidate.ArmyId == armyId &&
                candidate.ProductionQueue.Orders.Any(order =>
                    GameplayCatalog.Canonicalize(order.UnitTypeId) == GameplayCatalog.Canonicalize(request.UnitTypeId))))
        {
            return null;
        }

        PurchaseQuote quote = _pricing.GetQuote(new PurchaseRequest(
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
            !_armies.CanControl(request.SenderId, occupant.ArmyId) ||
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
            !_armies.CanControl(request.SenderId, container.ArmyId) ||
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
    private int NextAvailableTeamId()
    {
        int team = 1;
        while (_players().Any(player => player.TeamId == team)) team++;
        return team;
    }

    private int ConfirmTeamId(NetworkMessage request)
    {
        Guid playerId = request.PlayerId ?? request.SenderId;
        if (_players().Any(player => player.Id == playerId))
            return request.TeamId > 0 ? request.TeamId : NextAvailableTeamId();
        return NextAvailableTeamId();
    }

    /// <summary>Grants the requested skin unless another player already owns it.</summary>
    private PlayerSkin ConfirmPlayerSkin(NetworkMessage request)
    {
        Guid playerId = request.PlayerId ?? request.SenderId;
        Player[] otherPlayers = _players().Where(player => player.Id != playerId).ToArray();
        PlayerSkin requested = (PlayerSkin)(request.PlayerSkin ?? (int)PlayerSkin.Green);
        bool IsValid(PlayerSkin skin) => Enum.IsDefined(skin);
        bool IsTaken(PlayerSkin skin) => otherPlayers.Any(player => player.Skin == skin);

        if (IsValid(requested) && !IsTaken(requested))
            return requested;

        foreach (PlayerSkin skin in Enum.GetValues<PlayerSkin>())
            if (!IsTaken(skin))
                return skin;

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
            UpdateProduction(GameplayPacing.ScaleWork(
                (float)HostSimulationInterval, _isMatchStarted()));
            UpdateContainerEntries();
            _combat.Update(_hostTime, (float)HostSimulationInterval, command => _requestQueue.Enqueue(command));

            foreach (TiberiumSource source in _world.Units.Units.OfType<TiberiumSource>())
            {
                if (!source.TryTakePendingSeedCell(out Point cell) ||
                    !_world.Tiberium.TryHostSeed(cell, _hostTime, out TiberiumSeedState state))
                    continue;
                NetworkMessage seedCommand = NetworkCommands.CreateTiberiumSeedCommand(_networkHandler.LocalPeerId, state);
                _networkHandler.ApplyLocalCommand(seedCommand);
                _ = _networkHandler.BroadcastAsync(seedCommand, CancellationToken.None);
            }
        }

        int sentUpdates = 0;
        IReadOnlyList<Unit> phaseUnits = _world.Units.GetSnapshot();
        foreach (Building constructionSite in phaseUnits
            .OfType<Building>()
            .Where(site => site.NetworkStateDirty)
            .Concat(phaseUnits
                .OfType<Building>()
                .Where(site => !site.NetworkStateDirty && site.IsNetworkUpdateDue(_hostTime))))
        {
            if (sentUpdates >= MaximumStateUpdatesPerTick)
                break;

            NetworkMessage state = NetworkCommands.CreateUnitStateCommand(
                _networkHandler.LocalPeerId,
                constructionSite.GetState());
            _networkHandler.ApplyLocalCommand(state);
            _ = _networkHandler.BroadcastAsync(state, CancellationToken.None);
            constructionSite.MarkNetworkStateSent(_hostTime, StateHeartbeatInterval);
            sentUpdates++;
        }
    }

    private void UpdateProduction(float elapsedSeconds)
    {
        foreach (Building building in _world.Units.GetSnapshot().OfType<Building>())
        {
            if (building is TiberiumRefinery refinery && refinery.IsCompleted && !refinery.IncludedUnitGranted)
            {
                Guid ownerPlayerId = refinery.ArmyId is Guid armyId
                    ? _armies.Find(armyId)?.OwnerPlayerIds.OrderBy(id => id).FirstOrDefault() ?? Guid.Empty
                    : refinery.CreatorPlayerId;
                refinery.TryQueueIncludedHarvester(ownerPlayerId);
            }
            if (building is Helipad includedPad && includedPad.IsCompleted && !includedPad.IncludedUnitGranted)
            {
                Guid ownerPlayerId = includedPad.ArmyId is Guid armyId
                    ? _armies.Find(armyId)?.OwnerPlayerIds.OrderBy(id => id).FirstOrDefault() ?? Guid.Empty
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
                _armies.Find(researchArmyId) is Army researchArmy)
            {
                researchArmy.Perks.GrantPermanent(researchPerk, completedOrder.OrderId);
                NetworkMessage researchCompleted = NetworkCommands.CreateResearchCompletedCommand(
                    _networkHandler.LocalPeerId, researchArmyId, completedOrder.OrderId,
                    completedOrder.UnitTypeId);
                _networkHandler.ApplyLocalCommand(researchCompleted);
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
                _networkHandler.ApplyLocalCommand(delivery);
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
            _networkHandler.ApplyLocalCommand(command);
            _ = _networkHandler.BroadcastAsync(command, CancellationToken.None);
        }
    }

    private void UpdateContainerEntries()
    {
        foreach (MobileUnit occupant in _world.Units.GetSnapshot().OfType<MobileUnit>())
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
            _networkHandler.ApplyLocalCommand(command);
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


}
