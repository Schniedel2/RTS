using System;
using Microsoft.Xna.Framework;

namespace RTS.Network;

public static class NetworkCommands
{
    public static NetworkMessage CreateNotifyUnitsSelected(Guid senderId, Guid[] unitIds, uint revision) =>
        new(NetworkMessageType.NotifyUnitsSelected, senderId, PlayerId: senderId,
            UnitIds: unitIds, SelectionRevision: revision);

    public static NetworkMessage CreateArmyControlRequest(Guid senderId, Guid armyId, Guid recipientPlayerId, bool grant) =>
        new(grant ? NetworkMessageType.GrantArmyControlRequest : NetworkMessageType.RevokeArmyControlRequest,
            senderId, TargetId: recipientPlayerId, PlayerId: senderId, ArmyId: armyId);

    public static NetworkMessage CreateArmyControlCommand(Guid hostId, NetworkMessage request, bool grant) =>
        new(grant ? NetworkMessageType.GrantArmyControlCommand : NetworkMessageType.RevokeArmyControlCommand,
            hostId, TargetId: request.TargetId, PlayerId: request.PlayerId ?? request.SenderId, ArmyId: request.ArmyId);

    public static NetworkMessage CreateTransferUnitRequest(Guid senderId, Guid unitId, Guid recipientPlayerId) =>
        new(NetworkMessageType.TransferUnitRequest, senderId, TargetId: recipientPlayerId,
            PlayerId: senderId, UnitId: unitId);

    public static NetworkMessage CreateTransferUnitCommand(Guid hostId, NetworkMessage request, Guid recipientArmyId) =>
        new(NetworkMessageType.TransferUnitCommand, hostId, PlayerId: request.PlayerId ?? request.SenderId,
            UnitId: request.UnitId, ArmyId: recipientArmyId);

    public static NetworkMessage CreateMergeArmiesRequest(Guid senderId, Guid firstArmyId, Guid secondArmyId, Guid mergedArmyId) =>
        new(NetworkMessageType.MergeArmiesRequest, senderId, PlayerId: senderId,
            ArmyId: firstArmyId, SecondaryArmyId: secondArmyId, TargetId: mergedArmyId);

    public static NetworkMessage CreateTiberiumSeedCommand(Guid hostId, TiberiumSeedState state) =>
        new(NetworkMessageType.TiberiumSeedCommand, hostId, TiberiumSeed: state);

    public static NetworkMessage CreateMergeArmiesCommand(Guid hostId, NetworkMessage request) =>
        new(NetworkMessageType.MergeArmiesCommand, hostId, PlayerId: request.PlayerId ?? request.SenderId,
            ArmyId: request.ArmyId, SecondaryArmyId: request.SecondaryArmyId, TargetId: request.TargetId);

    public static NetworkMessage CreateWorldData(Guid hostId, WorldData worldData)
    {
        return new NetworkMessage(
            NetworkMessageType.WorldData,
            hostId,
            WorldData: worldData);
    }

    public static NetworkMessage CreateWorldDataRequest(Guid senderId)
    {
        return new NetworkMessage(
            NetworkMessageType.RequestWorldData,
            senderId);
    }

    public static NetworkMessage CreatePlayerUpdateRequest(Player player)
    {
        return new NetworkMessage(
            NetworkMessageType.RequestPlayerUpdate,
            player.Id,
            PlayerId: player.Id,
            DisplayName: player.Name,
            TeamId: player.TeamId,
            PlayerSkin: (int)player.Skin);
    }

    public static NetworkMessage CreatePlayerUpdateCommand(Guid hostId, NetworkMessage request, PlayerSkin confirmedSkin)
    {
        return new NetworkMessage(
            NetworkMessageType.PlayerUpdate,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            DisplayName: request.DisplayName,
            TeamId: request.TeamId,
            PlayerSkin: (int)confirmedSkin);
    }

    public static NetworkMessage CreateBuildCommand(Guid hostId, NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.BuildCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitId: request.UnitId ?? Guid.NewGuid(),
            UnitTypeId: request.UnitTypeId,
            X: request.X,
            Y: request.Y,
            Z: request.Z,
            TargetAngleY: request.TargetAngleY,
            ArmyId: request.ArmyId,
            ResourceAmount: request.ResourceAmount);
    }

    public static NetworkMessage CreateTextMessage(Guid senderId, string text)
    {
        return new NetworkMessage(
            NetworkMessageType.TextMessage,
            senderId,
            Text: text);
    }

    public static NetworkMessage CreateTextRequest(
        Guid senderId,
        string text,
        Guid? targetId = null)
    {
        return new NetworkMessage(
            NetworkMessageType.TextRequest,
            senderId,
            TargetId: targetId,
            Text: text);
    }

    public static NetworkMessage CreateTextCommand(
        Guid hostId,
        NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.TextMessage,
            hostId,
            TargetId: request.TargetId,
            Text: request.Text);
    }

    public static NetworkMessage CreateCommandToHost(
        Guid senderId,
        string command,
        string[] arguments)
    {
        return new NetworkMessage(
            NetworkMessageType.CommandToHost,
            senderId,
            Command: command,
            Arguments: arguments);
    }

    public static NetworkMessage CreateCommandToMember(
        Guid senderId,
        Guid memberId,
        string command,
        string[] arguments)
    {
        return new NetworkMessage(
            NetworkMessageType.CommandToMember,
            senderId,
            TargetId: memberId,
            Command: command,
            Arguments: arguments);
    }

    public static NetworkMessage CreateCommandToAll(
        Guid senderId,
        string command,
        string[] arguments)
    {
        return new NetworkMessage(
            NetworkMessageType.CommandToAll,
            senderId,
            Command: command,
            Arguments: arguments);
    }

    public static NetworkMessage CreateSpawnRequest(
        Guid senderId,
        string unitTypeId,
        float x,
        float y,
        float z)
    {
        return new NetworkMessage(
            NetworkMessageType.SpawnRequest,
            senderId,
            PlayerId: senderId,
            UnitTypeId: unitTypeId,
            X: x,
            Y: y,
            Z: z);
    }

    public static NetworkMessage CreateNeutralSpawnRequest(
        Guid senderId,
        string unitTypeId,
        float x,
        float y,
        float z) =>
        new(NetworkMessageType.SpawnRequest, senderId,
            PlayerId: Guid.Empty,
            UnitTypeId: unitTypeId,
            X: x,
            Y: y,
            Z: z);

    public static NetworkMessage CreateSpawnCommand(
        Guid hostId,
        NetworkMessage request)
    {
        Guid playerId = request.PlayerId ?? request.SenderId;
        return new NetworkMessage(
            NetworkMessageType.SpawnCommand,
            hostId,
            PlayerId: playerId,
            UnitId: request.UnitId ?? Guid.NewGuid(),
            UnitTypeId: request.UnitTypeId,
            X: request.X,
            Y: request.Y,
            Z: request.Z,
            DriverUnitId: playerId == Guid.Empty ? null : Guid.NewGuid());
    }

    public static NetworkMessage CreateGotoRequest(
        Guid senderId,
        Guid[] unitIds,
        float x,
        float y,
        float z,
        bool appendToQueue = false,
        UnitRoute[]? routes = null)
    {
        return new NetworkMessage(
            NetworkMessageType.GotoRequest,
            senderId,
            PlayerId: senderId,
            UnitIds: unitIds,
            X: x,
            Y: y,
            Z: z,
            AppendToQueue: appendToQueue,
            Routes: routes);
    }

    public static NetworkMessage CreateGotoCommand(
        Guid hostId,
        NetworkMessage request,
        UnitRoute[] routes)
    {
        return new NetworkMessage(
            NetworkMessageType.GotoCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds,
            X: request.X,
            Y: request.Y,
            Z: request.Z,
            AppendToQueue: request.AppendToQueue,
            Routes: routes);
    }

    public static NetworkMessage CreateStopRequest(Guid senderId, Guid[] unitIds) =>
        new(NetworkMessageType.StopRequest, senderId, PlayerId: senderId, UnitIds: unitIds);

    public static NetworkMessage CreateStopCommand(Guid hostId, NetworkMessage request) =>
        new(NetworkMessageType.StopCommand, hostId,
            PlayerId: request.PlayerId ?? request.SenderId, UnitIds: request.UnitIds);

    public static NetworkMessage CreateAttackRequest(
        Guid senderId,
        Guid[] unitIds,
        float x,
        float y,
        float z)
    {
        return new NetworkMessage(
            NetworkMessageType.AttackRequest,
            senderId,
            PlayerId: senderId,
            UnitIds: unitIds,
            X: x,
            Y: y,
            Z: z);
    }

    public static NetworkMessage CreateAttackCommand(Guid hostId, NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.AttackCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds,
            X: request.X,
            Y: request.Y,
            Z: request.Z);
    }

    public static NetworkMessage CreateProjectileSpawnCommand(
        Guid hostId,
        Guid projectileId,
        Guid attackerId,
        ProjectileKind kind,
        Vector3 position,
        Vector3 velocity,
        double serverTime) =>
        new(
            NetworkMessageType.ProjectileSpawnCommand,
            hostId,
            UnitId: attackerId,
            ProjectileId: projectileId,
            ProjectileKind: kind,
            X: position.X,
            Y: position.Y,
            Z: position.Z,
            VelocityX: velocity.X,
            VelocityY: velocity.Y,
            VelocityZ: velocity.Z,
            ServerTime: serverTime);

    public static NetworkMessage CreateProjectileImpactCommand(
        Guid hostId,
        Guid projectileId,
        Guid attackerId,
        Vector3 position,
        Vector3 normal,
        Guid? hitUnitId,
        double serverTime) =>
        new(
            NetworkMessageType.ProjectileImpactCommand,
            hostId,
            UnitId: attackerId,
            TargetId: hitUnitId,
            ProjectileId: projectileId,
            X: position.X,
            Y: position.Y,
            Z: position.Z,
            NormalX: normal.X,
            NormalY: normal.Y,
            NormalZ: normal.Z,
            ServerTime: serverTime);

    public static NetworkMessage CreateSetRallyPointRequest(Guid senderId, Guid unitId, Vector3? position) =>
        new(NetworkMessageType.SetRallyPointRequest, senderId, UnitId: unitId,
            RallyPoint: position is Vector3 point ? new(0, true, point.X, point.Y, point.Z) : new(0, false));

    public static NetworkMessage CreateSetRallyPointCommand(Guid hostId, Unit unit) =>
        new(NetworkMessageType.SetRallyPointCommand, hostId, UnitId: unit.UnitId, RallyPoint: unit.GetRallyPointState());

    public static NetworkMessage CreateProducedUnitCommand(
        Guid hostId,
        Building building,
        ProductionOrder order,
        Vector3 spawnPosition,
        Vector3 exitPosition)
    {
        Vector2 direction = new(
            exitPosition.X - spawnPosition.X,
            exitPosition.Z - spawnPosition.Z);
        float rotationDegrees = direction.LengthSquared() > 0.0001f
            ? MathHelper.ToDegrees(MathF.Atan2(-direction.X, -direction.Y))
            : 0.0f;

        return new NetworkMessage(
            NetworkMessageType.SpawnCommand,
            hostId,
            PlayerId: order.RequestedByPlayerId,
            UnitId: Guid.NewGuid(),
            UnitTypeId: order.UnitTypeId,
            TargetAngleY: rotationDegrees,
            X: spawnPosition.X,
            Y: spawnPosition.Y,
            Z: spawnPosition.Z,
            ArmyId: building.ArmyId,
            SpawnSourceBuildingId: building.UnitId,
            ExitX: exitPosition.X,
            ExitY: exitPosition.Y,
            ExitZ: exitPosition.Z,
            DriverUnitId: order.RequestedByPlayerId == Guid.Empty ? null : Guid.NewGuid(),
            RallyPoint: building.GetRallyPointState());
    }

    public static NetworkMessage CreateEnterUnitRequest(Guid senderId, Guid unitId, Guid containerId, OccupantRole? role = null) =>
        new(NetworkMessageType.EnterUnitRequest, senderId,
            PlayerId: senderId, UnitId: unitId, TargetId: containerId, OccupantRole: role);

    public static NetworkMessage CreateEnterUnitCommand(Guid hostId, NetworkMessage request, OccupantRole role) =>
        new(NetworkMessageType.EnterUnitCommand, hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitId: request.UnitId,
            TargetId: request.TargetId,
            OccupantRole: role);

    public static NetworkMessage CreateEmbarkUnitCommand(Guid hostId, Guid unitId, Guid containerId, OccupantRole role) =>
        new(NetworkMessageType.EmbarkUnitCommand, hostId,
            UnitId: unitId, TargetId: containerId, OccupantRole: role);

    public static NetworkMessage CreateLeaveContainerRequest(Guid senderId, Guid containerId) =>
        new(NetworkMessageType.LeaveContainerRequest, senderId, PlayerId: senderId, UnitId: containerId);

    public static NetworkMessage CreateLeaveContainerCommand(
        Guid hostId,
        Guid containerId,
        Guid occupantUnitId,
        Vector3 exitPosition) =>
        new(NetworkMessageType.LeaveContainerCommand, hostId,
            UnitId: containerId,
            TargetId: occupantUnitId,
            X: exitPosition.X,
            Y: exitPosition.Y,
            Z: exitPosition.Z);

    /// <summary>Host-authoritative visual impact for an instantaneous (hitscan) weapon.</summary>
    public static NetworkMessage CreateBulletImpactCommand(Guid hostId, Guid sourceUnitId, Vector3 position) =>
        new(NetworkMessageType.BulletImpactCommand, hostId,
            UnitId: sourceUnitId,
            X: position.X,
            Y: position.Y,
            Z: position.Z);

    public static NetworkMessage CreateAttackTargetRequest(Guid senderId, Guid[] unitIds, Guid targetId)
    {
        return new NetworkMessage(NetworkMessageType.AttackTargetRequest, senderId,
            PlayerId: senderId, UnitIds: unitIds, TargetId: targetId);
    }

    public static NetworkMessage CreateAttackTargetCommand(Guid hostId, NetworkMessage request)
    {
        return new NetworkMessage(NetworkMessageType.AttackTargetCommand, hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds, TargetId: request.TargetId);
    }

    public static NetworkMessage CreateFollowRequest(Guid senderId, Guid[] unitIds, Guid targetId) =>
        new(NetworkMessageType.FollowRequest, senderId,
            PlayerId: senderId, UnitIds: unitIds, TargetId: targetId);

    public static NetworkMessage CreateFollowCommand(Guid hostId, NetworkMessage request) =>
        new(NetworkMessageType.FollowCommand, hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds, TargetId: request.TargetId);

    public static NetworkMessage CreateAttackGroundRequest(Guid senderId, Guid[] unitIds, Vector3 target)
    {
        return new NetworkMessage(NetworkMessageType.AttackGroundRequest, senderId,
            PlayerId: senderId, UnitIds: unitIds, X: target.X, Y: target.Y, Z: target.Z);
    }

    public static NetworkMessage CreateAttackGroundCommand(Guid hostId, NetworkMessage request)
    {
        return new NetworkMessage(NetworkMessageType.AttackGroundCommand, hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds, X: request.X, Y: request.Y, Z: request.Z);
    }

    /// <summary>Host-only replication of automatic defensive targeting.</summary>
    public static NetworkMessage CreateTemporaryTargetCommand(
        Guid hostId,
        Guid unitId,
        Guid? targetId)
    {
        return new NetworkMessage(
            NetworkMessageType.TemporaryTargetCommand,
            hostId,
            TargetId: targetId,
            UnitIds: [unitId]);
    }

    public static NetworkMessage CreateUnitHitCommand(
        Guid hostId,
        Guid targetUnitId,
        Guid? sourceUnitId,
        Vector3 position,
        float damage,
        float hitPoints)
    {
        return new NetworkMessage(
            NetworkMessageType.UnitHitCommand,
            hostId,
            UnitId: targetUnitId,
            TargetId: sourceUnitId,
            X: position.X,
            Y: position.Y,
            Z: position.Z,
            Damage: damage,
            HitPoints: hitPoints);
    }

    public static NetworkMessage CreateDestroyUnitCommand(Guid hostId, Guid unitId)
    {
        return new NetworkMessage(
            NetworkMessageType.DestroyUnitCommand,
            hostId,
            UnitId: unitId);
    }

    public static NetworkMessage CreateToolActionRequest(
        Guid senderId,
        UnitAction action,
        ToolShape toolShape,
        int toolSize,
        float x,
        float y,
        float z,
        TerrainTile? terrainTile)
    {
        return new NetworkMessage(
            NetworkMessageType.ToolActionRequest,
            senderId,
            PlayerId: senderId,
            Action: action,
            ToolShape: toolShape,
            ToolSize: toolSize,
            X: x,
            Y: y,
            Z: z,
            TerrainTile: terrainTile);
    }

    public static NetworkMessage CreateBuildRequest(
        Guid senderId,
        string buildingTypeName,
        float x,
        float y,
        float z,
        float targetAngleY,
        Guid unitId)
    {
        NetworkMessage request = new NetworkMessage(
            NetworkMessageType.BuildRequest,
            senderId,
            PlayerId: senderId,
            X: x,
            Y: y,
            Z: z,
            UnitTypeId: buildingTypeName,
            TargetAngleY: targetAngleY,
            UnitId: unitId);
        return request;
    }

    public static NetworkMessage CreateToolActionCommand(
        Guid hostId,
        NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.ToolActionCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            Action: request.Action,
            ToolShape: request.ToolShape,
            ToolSize: request.ToolSize,
            X: request.X,
            Y: request.Y,
            Z: request.Z,
            TerrainTile: request.TerrainTile);
    }

    public static NetworkMessage CreateBuildConstructionRequest(
        Guid senderId,
        Guid[] unitIds,
        Guid constructionSiteId)
    {
        return new NetworkMessage(
            NetworkMessageType.BuildConstructionRequest,
            senderId,
            PlayerId: senderId,
            UnitIds: unitIds,
            ConstructionSiteId: constructionSiteId);
    }

    public static NetworkMessage CreateSellBuildingRequest(Guid senderId, Guid buildingId) =>
        new(NetworkMessageType.SellBuildingRequest, senderId, UnitId: buildingId);

    public static NetworkMessage CreateSellBuildingCommand(
        Guid hostId, Building building, int armyResources) =>
        new(NetworkMessageType.SellBuildingCommand, hostId,
            UnitId: building.UnitId, ArmyId: building.ArmyId, ResourceAmount: armyResources);

    public static NetworkMessage CreateBuildConstructionCommand(
        Guid hostId,
        NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.BuildConstructionCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds,
            ConstructionSiteId: request.ConstructionSiteId);
    }

    public static NetworkMessage CreateTrainUnitRequest(
        Guid senderId,
        Guid buildingId,
        string unitTypeId)
    {
        return new NetworkMessage(
            NetworkMessageType.TrainUnitRequest,
            senderId,
            PlayerId: senderId,
            UnitId: buildingId,
            UnitTypeId: unitTypeId,
            ProductionOrderId: Guid.NewGuid());
    }

    public static NetworkMessage CreateTrainUnitCommand(
        Guid hostId,
        NetworkMessage request,
        float productionSeconds)
    {
        return new NetworkMessage(
            NetworkMessageType.TrainUnitCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitId: request.UnitId,
            UnitTypeId: request.UnitTypeId,
            ProductionOrderId: request.ProductionOrderId ?? Guid.NewGuid(),
            ProductionSeconds: productionSeconds);
    }

    public static NetworkMessage CreateUnitStateCommand(
        Guid hostId,
        UnitState state)
    {
        return new NetworkMessage(
            NetworkMessageType.UnitStateCommand,
            hostId,
            UnitState: state);
    }
}
