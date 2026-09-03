using Microsoft.Xna.Framework;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

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
    
    public Task RequestGotoAsync(List<Unit> units, Vector3 position)
    {
        Guid[] unitIds = units.Select(unit => unit.UnitId).ToArray();
        return RequestGotoAsync(unitIds, position.X, position.Y, position.Z, CancellationToken.None);
    }

    public Task RequestGotoAsync(Guid[] unitIds, float x, float y, float z, CancellationToken cancellationToken = default)
    {
        NetworkMessage request = NetworkCommands.CreateGotoRequest( _networkHandler.LocalPeerId, unitIds, x, y, z);
        return _networkHandler.SendToHostAsync(request, cancellationToken);
    }

    public Task RequestToolActionAsync(UnitActionType action, ToolShape toolShape, int toolSize, Vector3 target)
    {
        return RequestToolActionAsync(action, toolShape, toolSize, target.X, target.Y, target.Z);
    }
    public Task RequestToolActionAsync(UnitActionType action, ToolShape toolShape, int toolSize, float x, float y, float z)
    {
        NetworkMessage request = NetworkCommands.CreateToolActionRequest(_networkHandler.LocalPeerId, action, toolShape, toolSize, x, y, z);
        return _networkHandler.SendToHostAsync(request);
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