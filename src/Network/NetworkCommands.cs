using System;
using Microsoft.Xna.Framework;

namespace RTS.Network;

public static class NetworkCommands
{
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
            PlayerColor: player.Color.PackedValue);
    }

    public static NetworkMessage CreatePlayerUpdateCommand(Guid hostId, NetworkMessage request, uint confirmedColor)
    {
        return new NetworkMessage(
            NetworkMessageType.PlayerUpdate,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            DisplayName: request.DisplayName,
            TeamId: request.TeamId,
            PlayerColor: confirmedColor);
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
            Z: request.Z);
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

    public static NetworkMessage CreateSpawnCommand(
        Guid hostId,
        NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.SpawnCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitId: request.UnitId ?? Guid.NewGuid(),
            UnitTypeId: request.UnitTypeId,
            X: request.X,
            Y: request.Y,
            Z: request.Z);
    }

    public static NetworkMessage CreateGotoRequest(
        Guid senderId,
        Guid[] unitIds,
        float x,
        float y,
        float z)
    {
        return new NetworkMessage(
            NetworkMessageType.GotoRequest,
            senderId,
            PlayerId: senderId,
            UnitIds: unitIds,
            X: x,
            Y: y,
            Z: z);
    }

    public static NetworkMessage CreateGotoCommand(
        Guid hostId,
        NetworkMessage request)
    {
        return new NetworkMessage(
            NetworkMessageType.GotoCommand,
            hostId,
            PlayerId: request.PlayerId ?? request.SenderId,
            UnitIds: request.UnitIds,
            X: request.X,
            Y: request.Y,
            Z: request.Z);
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
        float z)
    {
        NetworkMessage request = new NetworkMessage(
            NetworkMessageType.BuildRequest,
            senderId,
            PlayerId: senderId,
            X: x,
            Y: y,
            Z: z,
            UnitTypeId: buildingTypeName);
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
