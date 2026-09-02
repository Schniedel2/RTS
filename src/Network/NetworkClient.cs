using System.Threading;
using System.Threading.Tasks;
using System;

namespace RTS.Network;

public sealed class NetworkClient
{
    private readonly NetworkHandler _networkHandler;

    public NetworkClient(NetworkHandler networkHandler)
    {
        _networkHandler = networkHandler;
    }

    public Task RequestSpawnAsync(
        string unitTypeId,
        float x,
        float y,
        float z,
        CancellationToken cancellationToken = default)
    {
        NetworkMessage request = NetworkCommands.CreateSpawnRequest(
            _networkHandler.LocalPeerId,
            unitTypeId,
            x,
            y,
            z);

        return _networkHandler.SendToHostAsync(request, cancellationToken);
    }

    public Task RequestGotoAsync(
        Guid[] unitIds,
        float x,
        float y,
        float z,
        CancellationToken cancellationToken = default)
    {
        NetworkMessage request = NetworkCommands.CreateGotoRequest(
            _networkHandler.LocalPeerId,
            unitIds,
            x,
            y,
            z);

        return _networkHandler.SendToHostAsync(request, cancellationToken);
    }

    public Task RequestSayAsync(
        string text,
        Guid? targetId = null,
        CancellationToken cancellationToken = default)
    {
        NetworkMessage request = NetworkCommands.CreateTextRequest(
            _networkHandler.LocalPeerId,
            text,
            targetId);

        return _networkHandler.SendToHostAsync(request, cancellationToken);
    }
}