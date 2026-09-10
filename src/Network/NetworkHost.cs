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

                NetworkMessage command = request.Type switch
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
                    NetworkMessageType.RequestPlayerUpdate => NetworkCommands.CreatePlayerUpdateCommand(_networkHandler.LocalPeerId, request, ConfirmPlayerColor(request)),
                    NetworkMessageType.BuildRequest => NetworkCommands.CreateBuildCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.BuildConstructionRequest => NetworkCommands.CreateBuildConstructionCommand(_networkHandler.LocalPeerId, request),
                    _ => throw new InvalidOperationException($"Unsupported request type: {request.Type}")
                };

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

    /// <summary>Grants the requested color unless another player already owns it, otherwise picks the first free palette entry.</summary>
    private static uint ConfirmPlayerColor(NetworkMessage request)
    {
        Guid playerId = request.PlayerId ?? request.SenderId;
        Player[] otherPlayers = Globals.Game.Players.Where(player => player.Id != playerId).ToArray();

        bool IsTaken(uint packedColor) =>
            otherPlayers.Any(player => player.Color.PackedValue == packedColor);

        if (request.PlayerColor is uint requestedColor && !IsTaken(requestedColor))
            return requestedColor;

        foreach (Color paletteColor in Player.ColorPalette)
        {
            if (!IsTaken(paletteColor.PackedValue))
                return paletteColor.PackedValue;
        }

        return Player.ColorPalette[0].PackedValue;
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

            UpdateDefensiveTargets();

            foreach (Unit unit in _world.Units.Units.OfType<Unit>())
            {
                if (!unit.IsReadyToShoot(_hostTime))
                    continue;
                if (!unit.TryQueueShot(_hostTime, out MobileUnit? target) || target is null)
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
        foreach (ConstructionSite constructionSite in _world.Units.Units
            .OfType<ConstructionSite>()
            .Where(site => site.NetworkStateDirty)
            .Concat(_world.Units.Units
                .OfType<ConstructionSite>()
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
        const float damagePerAttacker = 25.0f;
        Vector3 impactPosition = new(request.X, request.Y, request.Z);

        foreach (Guid attackerId in request.UnitIds ?? Array.Empty<Guid>())
        {
            if (_world.Units.FindById(attackerId) is not Unit)
                continue;

            MobileUnit? target = _world.Units.Units.FirstOrDefault(unit =>
            {
                Vector2 offset = new(
                    unit.Position.X - impactPosition.X,
                    unit.Position.Z - impactPosition.Z);
                return offset.LengthSquared() <= attackRadius * attackRadius;
            });

            if (target is null)
                continue;

            HitInfo hit = new(attackerId, impactPosition, damagePerAttacker);
            bool destroyed = target.OnHit(hit);
            NetworkMessage hitCommand = NetworkCommands.CreateUnitHitCommand(
                _networkHandler.LocalPeerId,
                target.UnitId,
                attackerId,
                impactPosition,
                damagePerAttacker,
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
