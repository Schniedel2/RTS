using System;

namespace RTS.Network;

public sealed record SessionInfo(
    string SessionName,
    string HostName,
    string Address,
    int Port);

public enum DiscoveryMessageType
{
    Discover,
    Advertise
}

public sealed record DiscoveryMessage(
    DiscoveryMessageType Type,
    string? SessionName = null,
    string? HostName = null,
    int Port = 0);

public enum NetworkMessageType
{
    JoinSession,
    JoinAccepted,
    JoinRejected,
    MemberJoined,
    MemberLeft,
    CommandToHost,
    CommandToMember,
    CommandToAll,
    SpawnRequest,
    SpawnCommand,
    GotoRequest,
    GotoCommand,
    TextRequest,
    TextMessage,
    Error,
    ToolActionRequest,
    ToolActionCommand,
    RequestWorldData,
    WorldData,
    RequestPlayerUpdate,
    PlayerUpdate,
    BuildRequest,
    BuildCommand,
    BuildConstructionRequest,
    BuildConstructionCommand
}

public sealed record WorldData(
    int Width,
    int Height,
    byte[] TileMap,
    float[] HeightMap);

public sealed record NetworkMessage(
    NetworkMessageType Type,
    Guid SenderId,
    Guid? TargetId = null,
    string? Command = null,
    string[]? Arguments = null,
    string? DisplayName = null,
    Guid? SessionId = null,
    string? Error = null,
    string? Text = null,
    Guid? PlayerId = null,
    Guid? UnitId = null,
    Guid[]? UnitIds = null,
    Guid? ConstructionSiteId = null,
    string? UnitTypeId = null,
    float X = 0.0f,
    float Y = 0.0f,
    float Z = 0.0f,
    ToolShape? ToolShape = null,
    int ToolSize = 0,
    UnitAction? Action = null,
    TerrainTile? TerrainTile = null,
    WorldData? WorldData = null,
    int TeamId = 0);
