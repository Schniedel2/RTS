using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;

namespace RTS.Network;

public sealed class NetworkHost
{
    private readonly NetworkHandler _networkHandler;
    private readonly ConcurrentQueue<NetworkMessage> _requestQueue = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private const int MaximumQueuedRequests = 1024;
    private const int MaximumRequestsPerUpdate = 32;

    public NetworkHost(NetworkHandler networkHandler, NetworkInput networkInput)
    {
        _networkHandler = networkHandler;
        networkInput.MessageReceived += HandleMessage;
    }

    private void HandleMessage(NetworkMessage message)
    {
        if (!_networkHandler.IsHost)
            return;

        if (message.Type != NetworkMessageType.SpawnRequest &&
            message.Type != NetworkMessageType.GotoRequest &&
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

    public async Task UpdateAsync()
    {
        if (!await _updateGate.WaitAsync(0))
            return;

        try
        {
            for (int index = 0; index < MaximumRequestsPerUpdate; index++)
            {
                if (!_requestQueue.TryDequeue(out NetworkMessage? request))
                    return;

                NetworkMessage command = request.Type switch
                {
                    NetworkMessageType.SpawnRequest => NetworkCommands.CreateSpawnCommand(_networkHandler.LocalPeerId, request),
                    NetworkMessageType.GotoRequest => NetworkCommands.CreateGotoCommand(_networkHandler.LocalPeerId, request),
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
}
