using System;

namespace RTS.Network;

public static class NetworkCommands
{
    public const string Spawn = "spawn";
    public const string Goto = "goto";

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

    public static NetworkMessage CreateToolActionRequest(
        Guid senderId,
        UnitActionType action,
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
}