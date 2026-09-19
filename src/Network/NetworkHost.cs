using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;
using System.Linq;
using Microsoft.Xna.Framework;

namespace RTS.Network;

public sealed class NetworkHost
{
    private readonly NetworkHandler _networkHandler;
    private readonly GameWorld _world;
    private readonly ConcurrentQueue<NetworkMessage> _requestQueue = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private const int MaximumQueuedRequests = 1024;
    private const int MaximumRequestsPerUpdate = 32;
    private const int MaximumStateUpdatesPerTick = 32;
    private const double HostSimulationInterval = 0.1;
    private const double StateHeartbeatInterval = 3.0;
    private double _hostTime;
    private double _simulationAccumulator;

    public NetworkHost(
        NetworkHandler networkHandler,
        NetworkInput networkInput,
        GameWorld world)
    {
        _networkHandler = networkHandler;
        _world = world;
        networkInput.MessageReceived += HandleMessage;
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
            message.Type != NetworkMessageType.BuildRequest &&
            message.Type != NetworkMessageType.BuildConstructionRequest &&
            message.Type != NetworkMessageType.TrainUnitRequest &&
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

            for (int index = 0; index < MaximumRequestsPerUpdate; index++)
            {
                if (!_requestQueue.TryDequeue(out NetworkMessage? request))
                    return;

                NetworkMessage? command = request.Type switch
                {
                    NetworkMessageType.SpawnRequest => NetworkCommands.CreateSpawnCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.GotoRequest => NetworkCommands.CreateGotoCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.StopRequest => NetworkCommands.CreateStopCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.AttackRequest => NetworkCommands.CreateAttackCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.AttackTargetRequest => NetworkCommands.CreateAttackTargetCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.AttackGroundRequest => NetworkCommands.CreateAttackGroundCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.FollowRequest => NetworkCommands.CreateFollowCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.TextRequest => NetworkCommands.CreateTextCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.ToolActionRequest => NetworkCommands.CreateToolActionCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.RequestPlayerUpdate => NetworkCommands.CreatePlayerUpdateCommand(_networkHandler.LocalPeerId, request, ConfirmPlayerSkin(request)),
                    NetworkMessageType.BuildRequest => NetworkCommands.CreateBuildCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.BuildConstructionRequest => NetworkCommands.CreateBuildConstructionCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.TrainUnitRequest => TryCreateTrainUnitCommand(request),
                    NetworkMessageType.EnterUnitRequest => TryCreateEnterUnitCommand(request),
                    NetworkMessageType.LeaveContainerRequest => TryCreateLeaveContainerCommand(request),
                    NetworkMessageType.NotifyUnitsSelected => request,
                    NetworkMessageType.GrantArmyControlRequest => NetworkCommands.CreateArmyControlCommand(_networkHandler.LocalPeerId, request, grant: true),
                    NetworkMessageType.RevokeArmyControlRequest => NetworkCommands.CreateArmyControlCommand(_networkHandler.LocalPeerId, request, grant: false),
                    NetworkMessageType.TransferUnitRequest => CreateTransferUnitCommand(request),
                    NetworkMessageType.MergeArmiesRequest => NetworkCommands.CreateMergeArmiesCommand(_networkHandler.LocalPeerId, request),
                    _ => throw new InvalidOperationException($"Unsupported request type: {request.Type}")
                };

                if (command is null)
                    continue;

                _networkHandler.EnqueueLocalMessage(command);
                await _networkHandler.BroadcastAsync(command, CancellationToken.None);

                if (request.Type == NetworkMessageType.AttackRequest)
                    await ResolveGroundAttackAsync(request);
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

    private NetworkMessage? TryCreateTrainUnitCommand(NetworkMessage request)
    {
        if (request.UnitId is not Guid buildingId ||
            string.IsNullOrWhiteSpace(request.UnitTypeId) ||
            _world.Units.FindById(buildingId) is not Building building ||
            !building.IsCompleted ||
            !Globals.Game.Armies.CanControl(request.SenderId, building.ArmyId) ||
            !building.TryGetProductionDuration(request.UnitTypeId, out float durationSeconds))
        {
            return null;
        }

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

        return NetworkCommands.CreateTrainUnitCommand(
            _networkHandler.LocalPeerId,
            request with { ProductionOrderId = orderId },
            durationSeconds);
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
                unit.UpdateHost(gameTime);
            }

            UpdateProduction((float)HostSimulationInterval);
            UpdateContainerEntries();

            UpdateDefensiveTargets();

            foreach (Unit unit in _world.Units.Units.OfType<Unit>())
            {
                if (!unit.IsReadyToShoot(_hostTime))
                    continue;
                if (!unit.TryQueueShot(_hostTime, out Unit? target) || target is null)
                    continue;
                _requestQueue.Enqueue(NetworkCommands.CreateAttackRequest(
                    _networkHandler.LocalPeerId,
                    [unit.UnitId],
                    target.Position.X, target.Position.Y, target.Position.Z));
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

    private void UpdateProduction(float elapsedSeconds)
    {
        foreach (Building building in _world.Units.Units.OfType<Building>().ToArray())
        {
            if (!building.UpdateProduction(elapsedSeconds, out ProductionOrder? completedOrder) ||
                completedOrder is null)
            {
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
        const float attackRadius = 1.5f;
        Vector3 impactPosition = new(request.X, request.Y, request.Z);

        foreach (Guid attackerId in request.UnitIds ?? Array.Empty<Guid>())
        {
            if (_world.Units.FindById(attackerId) is not Unit attacker || attacker.IsDying)
                continue;

            if (attacker.UsesHitscanWeapon)
            {
                NetworkMessage impactCommand = NetworkCommands.CreateBulletImpactCommand(
                    _networkHandler.LocalPeerId, attackerId, impactPosition);
                _networkHandler.EnqueueLocalMessage(impactCommand);
                await _networkHandler.BroadcastAsync(impactCommand, CancellationToken.None);
            }

            Unit? target = _world.Units.Units.FirstOrDefault(unit =>
            {
                if (!unit.CanBeTargeted)
                    return false;
                Vector2 offset = new(
                    unit.Position.X - impactPosition.X,
                    unit.Position.Z - impactPosition.Z);
                return offset.LengthSquared() <= attackRadius * attackRadius;
            });

            if (target is null)
                continue;

            HitInfo hit = new(attackerId, impactPosition, attacker.AttackDamage);
            bool destroyed = target.OnHit(hit);
            NetworkMessage hitCommand = NetworkCommands.CreateUnitHitCommand(
                _networkHandler.LocalPeerId,
                target.UnitId,
                attackerId,
                impactPosition,
                attacker.AttackDamage,
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
}
