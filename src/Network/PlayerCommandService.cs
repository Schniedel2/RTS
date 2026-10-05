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
    public LocalRequestReceipt? LastRequest { get; private set; }

    public PlayerCommandService(NetworkHandler network, Guid playerId)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        PlayerId = playerId;
    }

    public Task GotoAsync(IEnumerable<Guid> unitIds, Vector3 target, bool appendToQueue = false,
        UnitRoute[]? routes = null, CancellationToken cancellationToken = default,
        float? formationFacingDegrees = null)
    {
        if (!IsFinite(target) || (formationFacingDegrees.HasValue && !float.IsFinite(formationFacingDegrees.Value)))
            return Task.CompletedTask;
        Guid[] recipients = unitIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (recipients.Length == 0)
            return Task.CompletedTask;
        return SendAsync(NetworkCommands.CreateGotoRequest(PlayerId, recipients,
            target.X, target.Y, target.Z, appendToQueue, routes, formationFacingDegrees), cancellationToken);
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
        return LastRequest?.Request.UnitId ?? buildingId;
    }

    public Task ConstructAsync(IEnumerable<Guid> workerIds, Guid constructionSiteId,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateBuildConstructionRequest(PlayerId, workerIds.ToArray(),
            constructionSiteId), cancellationToken);

    public async Task<Guid> BuildAndConstructAsync(string buildingTypeName, Vector3 target,
        float rotationDegrees, IEnumerable<Guid> workerIds, Guid? buildingId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buildingTypeName) || !IsFinite(target) || !float.IsFinite(rotationDegrees))
            return Guid.Empty;
        Guid id = buildingId ?? Guid.NewGuid();
        NetworkMessage request = NetworkCommands.CreateBuildRequest(PlayerId, buildingTypeName,
            target.X, target.Y, target.Z, rotationDegrees, id, workerIds.Distinct().ToArray());
        await SendAsync(request, cancellationToken);
        return LastRequest?.Request.UnitId ?? id;
    }

    public Task HarvestAsync(Guid harvesterId, Vector3 target,
        CancellationToken cancellationToken = default) =>
        IsFinite(target)
            ? SendAsync(NetworkCommands.CreateHarvestRequest(PlayerId, harvesterId, target), cancellationToken)
            : Task.CompletedTask;

    public Task ReturnHarvesterAsync(Guid harvesterId, CancellationToken cancellationToken = default) =>
        SendAsync(new NetworkMessage(NetworkMessageType.HarvesterReturnRequest, PlayerId, UnitId: harvesterId),
            cancellationToken);

    public Task TrainUnitAsync(Guid buildingId, string unitTypeId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(unitTypeId)
            ? Task.CompletedTask
            : SendAsync(NetworkCommands.CreateTrainUnitRequest(PlayerId, buildingId, unitTypeId),
                cancellationToken);

    public Task ResearchAsync(Guid buildingId, string projectId,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(projectId)
            ? Task.CompletedTask
            : SendAsync(NetworkCommands.CreateResearchRequest(PlayerId, buildingId, projectId),
                cancellationToken);

    public Task SetRallyPointAsync(Guid buildingId, Vector3? target,
        CancellationToken cancellationToken = default) =>
        target is not Vector3 point || IsFinite(point)
            ? SendAsync(NetworkCommands.CreateSetRallyPointRequest(PlayerId, buildingId, target),
                cancellationToken)
            : Task.CompletedTask;

    public Task AttackTerrainAsync(IEnumerable<Guid> unitIds, Vector3 target,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateAttackGroundRequest(PlayerId, unitIds.ToArray(), target), cancellationToken);

    public Task AttackTargetAsync(IEnumerable<Guid> unitIds, Guid targetId,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateAttackTargetRequest(PlayerId, unitIds.ToArray(), targetId), cancellationToken);

    public Task FollowAsync(IEnumerable<Guid> unitIds, Guid targetId,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateFollowRequest(PlayerId, unitIds.ToArray(), targetId), cancellationToken);

    public Task EnterUnitAsync(Guid unitId, Guid containerId, OccupantRole? role = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateEnterUnitRequest(PlayerId, unitId, containerId, role),
            cancellationToken);

    public Task MoveAwayAsync(Guid unitId, Vector3 fromPosition,
        CancellationToken cancellationToken = default) =>
        SendAsync(new NetworkMessage(NetworkMessageType.MoveAwayRequest, PlayerId, UnitId: unitId,
            X: fromPosition.X, Y: fromPosition.Y, Z: fromPosition.Z), cancellationToken);

    public Task CancelConstructionAsync(Guid buildingId, CancellationToken cancellationToken = default) =>
        SendAsync(NetworkCommands.CreateCancelConstructionRequest(PlayerId, buildingId), cancellationToken);

    private Task SendAsync(NetworkMessage request, CancellationToken cancellationToken)
    {
        LastRequest = _network.TrackLocalRequest(request);
        if (LastRequest is not null && !ReferenceEquals(LastRequest.Request, request))
            return Task.CompletedTask;
        if (!_network.AllowLocalAIRequest(request))
        {
            _network.ResolveLocalRequest(request, false, "AI recovery cooldown prevents repeating the failed order.", AIOrderFailure.RecoveryCooldown);
            return Task.CompletedTask;
        }
        if (_network.RouteLocalAIOrder(request)) return Task.CompletedTask;
        return _network.SendToHostAsync(request, cancellationToken);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
