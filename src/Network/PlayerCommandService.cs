using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RTS.Network;

/// <summary>
/// Actor-bound gateway for gameplay orders. Human input and host-side AI use
/// the same request creation and host transport through this service.
/// </summary>
public sealed class PlayerCommandService
{
    private readonly NetworkHandler _network;

    public Guid PlayerId { get; }

    public PlayerCommandService(NetworkHandler network, Guid playerId)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        PlayerId = playerId;
    }

    public Task GotoAsync(IEnumerable<Guid> unitIds, Vector3 target, bool appendToQueue = false,
        UnitRoute[]? routes = null, CancellationToken cancellationToken = default)
    {
        if (!IsFinite(target))
            return Task.CompletedTask;
        return SendAsync(NetworkCommands.CreateGotoRequest(PlayerId, unitIds.ToArray(),
            target.X, target.Y, target.Z, appendToQueue, routes), cancellationToken);
    }

    public Task StopAsync(IEnumerable<Guid> unitIds, CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateStopRequest(PlayerId, unitIds.ToArray()), cancellationToken);

    public Task ExecuteActionAsync(IEnumerable<Guid> unitIds, UnitActionType actionType,
        UnitActionContext? context = null, CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateUnitActionRequest(PlayerId, unitIds.ToArray(), actionType, context),
            cancellationToken);

    public Task<Guid> BuildAsync(string buildingTypeName, Vector3 target, float rotationDegrees,
        Guid? buildingId = null, CancellationToken cancellationToken = default)
    {
        Guid id = buildingId ?? Guid.NewGuid();
        if (string.IsNullOrWhiteSpace(buildingTypeName) || !IsFinite(target) || !float.IsFinite(rotationDegrees))
            return Task.FromResult(Guid.Empty);
        return SendBuildAsync(buildingTypeName, target, rotationDegrees, id, cancellationToken);
    }

    private async Task<Guid> SendBuildAsync(string buildingTypeName, Vector3 target, float rotationDegrees,
        Guid buildingId, CancellationToken cancellationToken)
    {
        await SendAsync(NetworkCommands.CreateBuildRequest(PlayerId, buildingTypeName,
            target.X, target.Y, target.Z, rotationDegrees, buildingId), cancellationToken);
        return buildingId;
    }

    public Task ConstructAsync(IEnumerable<Guid> workerIds, Guid constructionSiteId,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateBuildConstructionRequest(PlayerId, workerIds.ToArray(),
            constructionSiteId), cancellationToken);

    public async Task<Guid> BuildAndConstructAsync(string buildingTypeName, Vector3 target,
        float rotationDegrees, IEnumerable<Guid> workerIds, Guid? buildingId = null,
        CancellationToken cancellationToken = default)
    {
        Guid id = await BuildAsync(buildingTypeName, target, rotationDegrees, buildingId, cancellationToken);
        if (id != Guid.Empty)
            await ConstructAsync(workerIds, id, cancellationToken);
        return id;
    }

    public Task HarvestAsync(Guid harvesterId, Vector3 target,
        CancellationToken cancellationToken = default) =>
        IsFinite(target)
            ? SendAsync(NetworkCommands.CreateHarvestRequest(PlayerId, harvesterId, target), cancellationToken)
            : Task.CompletedTask;

    public Task ReturnHarvesterAsync(Guid harvesterId, CancellationToken cancellationToken = default) =>
        SendAsync(new NetworkMessage(NetworkMessageType.HarvesterReturnRequest, PlayerId, UnitId: harvesterId),
            cancellationToken);

    public Task AttackTerrainAsync(IEnumerable<Guid> unitIds, Vector3 target,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateAttackGroundRequest(PlayerId, unitIds.ToArray(), target), cancellationToken);

    public Task AttackTargetAsync(IEnumerable<Guid> unitIds, Guid targetId,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateAttackTargetRequest(PlayerId, unitIds.ToArray(), targetId), cancellationToken);

    public Task FollowAsync(IEnumerable<Guid> unitIds, Guid targetId,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateFollowRequest(PlayerId, unitIds.ToArray(), targetId), cancellationToken);

    public Task MoveAwayAsync(Guid unitId, Vector3 fromPosition,
        CancellationToken cancellationToken = default) =>
        SendAsync(new NetworkMessage(NetworkMessageType.MoveAwayRequest, PlayerId, UnitId: unitId,
            X: fromPosition.X, Y: fromPosition.Y, Z: fromPosition.Z), cancellationToken);

    private Task SendAsync(NetworkMessage request, CancellationToken cancellationToken) =>
        _network.SendToHostAsync(request, cancellationToken);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
