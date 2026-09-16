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

    public Task RequestWorldDataAsync(CancellationToken cancellationToken = default)
    {
        return _networkHandler.RequestWorldDataAsync(cancellationToken);
    }

    private uint _selectionRevision;

    public Task NotifyUnitsSelectedAsync(Guid[] unitIds, CancellationToken cancellationToken = default)
    {
        NetworkMessage message = NetworkCommands.CreateNotifyUnitsSelected(
            _networkHandler.LocalPeerId, unitIds, ++_selectionRevision);
        return _networkHandler.SendToHostAsync(message, cancellationToken);
    }

    public Task RequestArmyControlAsync(Guid armyId, Guid recipientPlayerId, bool grant) =>
        _networkHandler.SendToHostAsync(NetworkCommands.CreateArmyControlRequest(
            _networkHandler.LocalPeerId, armyId, recipientPlayerId, grant), CancellationToken.None);

    public Task RequestTransferUnitAsync(Guid unitId, Guid recipientPlayerId) =>
        _networkHandler.SendToHostAsync(NetworkCommands.CreateTransferUnitRequest(
            _networkHandler.LocalPeerId, unitId, recipientPlayerId), CancellationToken.None);

    public Task RequestMergeArmiesAsync(Guid firstArmyId, Guid secondArmyId) =>
        _networkHandler.SendToHostAsync(NetworkCommands.CreateMergeArmiesRequest(
            _networkHandler.LocalPeerId, firstArmyId, secondArmyId, Guid.NewGuid()), CancellationToken.None);

    public Task RequestPlayerUpdateAsync(
        Player player,
        CancellationToken cancellationToken = default)
    {
        return _networkHandler.SendToHostAsync(
            NetworkCommands.CreatePlayerUpdateRequest(player),
            cancellationToken);
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

    public Task RequestBuildAsync(string buildingTypeName, Vector3 target, float targetAngleY, Guid unitId)
    {
        return RequestBuildAsync(buildingTypeName, target.X, target.Y, target.Z, targetAngleY, unitId);
    }

    public Task RequestBuildAsync(string buildingTypeName, float x, float y, float z, float targetAngleY, Guid unitId)
    {
        NetworkMessage request = NetworkCommands.CreateBuildRequest(_networkHandler.LocalPeerId, buildingTypeName, x, y, z, targetAngleY, unitId);
        return _networkHandler.SendToHostAsync(request, CancellationToken.None);
    }

    public Task RequestStopAsync(IEnumerable<Unit> units)
    {
        return _networkHandler.SendToHostAsync(NetworkCommands.CreateStopRequest(
            _networkHandler.LocalPeerId, units.Select(unit => unit.UnitId).ToArray()));
    }

    public Task RequestBuildConstructionAsync(
        IEnumerable<Unit> units,
        Guid constructionSiteId,
        CancellationToken cancellationToken = default)
    {
        Guid[] unitIds = units.Select(unit => unit.UnitId).ToArray();
        NetworkMessage request = NetworkCommands.CreateBuildConstructionRequest(
            _networkHandler.LocalPeerId,
            unitIds,
            constructionSiteId);
        return _networkHandler.SendToHostAsync(request, cancellationToken);
    }

    /// <summary>Host-side tooling entry point for spawning a unit owned by another player, such as an AI.</summary>
    public Task RequestSpawnForPlayerAsync(
        Guid playerId,
        string unitTypeId,
        float x,
        float y,
        float z,
        CancellationToken cancellationToken = default)
    {
        NetworkMessage request = new(
            NetworkMessageType.SpawnRequest,
            _networkHandler.LocalPeerId,
            PlayerId: playerId,
            UnitTypeId: unitTypeId,
            X: x,
            Y: y,
            Z: z);
        return _networkHandler.SendToHostAsync(request, cancellationToken);
    }

    public Task RequestAttackTerrainAsync(IEnumerable<Unit> units, Vector3 target)
    {
        NetworkMessage request = NetworkCommands.CreateAttackGroundRequest(
            _networkHandler.LocalPeerId,
            units.Select(unit => unit.UnitId).ToArray(),
            target);
        return _networkHandler.SendToHostAsync(request, CancellationToken.None);
    }

    public Task RequestAttackTargetAsync(IEnumerable<Unit> units, Guid targetId)
    {
        NetworkMessage request = NetworkCommands.CreateAttackTargetRequest(
            _networkHandler.LocalPeerId,
            units.Select(unit => unit.UnitId).ToArray(),
            targetId);
        return _networkHandler.SendToHostAsync(request, CancellationToken.None);
    }

    public Task RequestFollowAsync(IEnumerable<Unit> units, Guid targetId)
    {
        NetworkMessage request = NetworkCommands.CreateFollowRequest(
            _networkHandler.LocalPeerId, units.Select(unit => unit.UnitId).ToArray(), targetId);
        return _networkHandler.SendToHostAsync(request, CancellationToken.None);
    }

    public Task RequestToolActionAsync(UnitAction action, ToolShape toolShape, int toolSize, Vector3 target)
    {
        return RequestToolActionAsync(action, toolShape, toolSize, target.X, target.Y, target.Z, null);
    }

    public Task RequestToolActionAsync(UnitAction action, ToolShape toolShape, int toolSize, Vector3 target, TerrainTile terrainTile)
    {
        return RequestToolActionAsync(action, toolShape, toolSize, target.X, target.Y, target.Z, terrainTile);
    }
    public Task RequestToolActionAsync(UnitAction action, ToolShape toolShape, int toolSize, float x, float y, float z, TerrainTile? terrainTile)
    {
        NetworkMessage request = NetworkCommands.CreateToolActionRequest(_networkHandler.LocalPeerId, action, toolShape, toolSize, x, y, z, terrainTile);
        return _networkHandler.SendToHostAsync(request, CancellationToken.None);
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
