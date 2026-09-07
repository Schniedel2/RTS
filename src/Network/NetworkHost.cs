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
            message.Type != NetworkMessageType.AttackRequest &&
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
                    NetworkMessageType.AttackRequest => NetworkCommands.CreateAttackCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.TextRequest => NetworkCommands.CreateTextCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.ToolActionRequest => NetworkCommands.CreateToolActionCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.RequestPlayerUpdate => NetworkCommands.CreatePlayerUpdateCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.BuildRequest => NetworkCommands.CreateBuildCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.BuildConstructionRequest => NetworkCommands.CreateBuildConstructionCommand(_networkHandler.LocalPeerId, request),
                    _ => throw new InvalidOperationException($"Unsupported request type: {request.Type}")
                };

                _networkHandler.EnqueueLocalMessage(command);
                await _networkHandler.BroadcastAsync(command, CancellationToken.None);
            }
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private void UpdateHostSimulation(GameTime gameTime)
    {
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
}
