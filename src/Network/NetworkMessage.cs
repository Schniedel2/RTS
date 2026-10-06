using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

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
    StopRequest,
    StopCommand,
    AttackRequest,
    AttackCommand,
    ProjectileSpawnCommand,
    ProjectileImpactCommand,
    BulletImpactCommand,
    AttackTargetRequest,
    AttackTargetCommand,
    AttackGroundRequest,
    AttackGroundCommand,
    FollowRequest,
    FollowCommand,
    TemporaryTargetCommand,
    UnitHitCommand,
    DestroyUnitCommand,
    TextRequest,
    TextMessage,
    Error,
    ToolActionRequest,
    ToolActionCommand,
    UnitActionRequest,
    UnitActionCommand,
    RequestWorldData,
    WorldData,
    RequestPlayerUpdate,
    PlayerUpdate,
    BuildRequest,
    BuildCommand,
    BuildConstructionRequest,
    BuildConstructionCommand,
    TrainUnitRequest,
    TrainUnitCommand,
    ResearchRequest,
    ResearchCommand,
    ResearchCompletedCommand,
    EnterUnitRequest,
    EnterUnitCommand,
    EmbarkUnitCommand,
    LeaveContainerRequest,
    LeaveContainerCommand,
    UnitStateCommand,
    NotifyUnitsSelected,
    GrantArmyControlRequest,
    GrantArmyControlCommand,
    RevokeArmyControlRequest,
    RevokeArmyControlCommand,
    TransferUnitRequest,
    TransferUnitCommand,
    MergeArmiesRequest,
    MergeArmiesCommand,
    SetRallyPointRequest,
    SetRallyPointCommand,
    EarthworkRequest,
    EarthworkStartCommand,
    EarthworkCellCommand,
    EarthworkEndCommand,
    HelicopterOrderRequest,
    TiberiumSeedCommand,
    HarvestRequest,
    HarvestCommand,
    TiberiumHarvestCommand,
    ArmyResourcesCommand,
    MoveAwayRequest,
    HarvesterReturnRequest,
    CancelConstructionRequest,
    SellBuildingRequest,
    DestroyBuildingRequest,
    CancelConstructionCommand,
    SellBuildingCommand,
    StartPositionWishRequest,
    StartMultiplayerGameRequest,
    StartMultiplayerGameCommand,
    SessionSnapshot,
    SessionReady,
    SyncDiagnosticsControlCommand,
    SyncDiagnosticsProbeCommand,
    SyncDiagnosticsReportRequest,
    ExploredVisibilityCommand,
    SatelliteReconRequest,
    SatelliteReconCommand,
    RequestFeedbackCommand,
    RequestStatusRequest,
    AIControllerAssignmentCommand,
    AIControllerHeartbeat
}

public sealed record WorldData(
    int Width,
    int Height,
    byte[] TileMap,
    float[] HeightMap,
    GameplayMarkerState[]? GameplayMarkers = null,
    TiberiumSeedState[]? TiberiumCells = null,
    MapObjectState[]? MapObjects = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UnitRoute(
    [property: JsonRequired] Guid UnitId,
    [property: JsonRequired] Point[] Cells,
    float? TargetX = null,
    float? TargetZ = null);

public sealed record MatchStartAssignment(
    Guid PlayerId,
    Guid ArmyId,
    int StartPositionSlot,
    float X,
    float Y,
    float Z,
    float RotationDegrees,
    Guid BulldozerId,
    bool IsAI = false,
    Guid? DriverUnitId = null);

public sealed record PerkSourceSnapshot(Guid SourceId, PerkGrant[] Grants);
public sealed record ArmySnapshot(Guid Id, Guid? TeamId, int Resources, Guid[] Owners,
    Dictionary<Guid, ArmyPermission> Permissions, IntelligenceCapabilities Intelligence,
    PerkSourceSnapshot[] Perks, SatelliteReconState? SatelliteRecon = null);
public sealed record VisibilitySnapshot(Guid ArmyId, byte[] Cells);
public sealed record ExploredVisibilitySnapshot(Guid ArmyId, int CellCount, byte[] Bits);
public sealed record OccupantSnapshot(Guid UnitId, OccupantRole Role);
public sealed record BuildingExitSnapshot(Guid SourceBuildingId, float X, float Y, float Z);
public sealed record RuntimeUnitSnapshot(string TypeId, Guid UnitId, Guid CreatorPlayerId, Guid? ArmyId,
    float X, float Y, float Z, float RotationDegrees, float HitPoints, UnitBehavior Behavior,
    int PurchasePrice, UnitState State, OccupantSnapshot[] Occupants,
    HarvestPhase? HarvestPhase = null, float CargoAmount = 0.0f, BuildingExitSnapshot? BuildingExit = null,
    EarthworkOrder? EarthworkOrder = null, int EarthworkSequence = 0, Soldier.Weapon? SoldierWeapon = null);
public sealed record SessionSnapshot(WorldData World, ArmySnapshot[] Armies,
    RuntimeUnitSnapshot[] Units, VisibilitySnapshot[] Visibility, double HostTime,
    bool IsMatchStarted = false, AIControllerAssignment[]? AIControllers = null);
public sealed record SyncDiagnosticDigest(long Sequence, double HostTime,
    Dictionary<string, string> Categories, Dictionary<string, string> Items,
    Dictionary<Guid, SyncDiagnosticPose>? UnitPoses = null,
    Dictionary<string, string>? Details = null,
    Dictionary<Guid, byte[]>? ExploredVisibility = null);
public sealed record SyncDiagnosticPose(float X, float Y, float Z, float YawDegrees);
public sealed record SyncDiagnosticReport(long Sequence, bool Matches,
    string[] Differences, Dictionary<string, string> Categories);

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
    UnitState? UnitState = null,
    float Damage = 0.0f,
    float HitPoints = 0.0f,
    string? UnitTypeId = null,
    float TargetAngleY = 0.0f,
    float X = 0.0f,
    float Y = 0.0f,
    float Z = 0.0f,
    ToolShape? ToolShape = null,
    int ToolSize = 0,
    UnitAction? Action = null,
    UnitActionType? UnitActionType = null,
    UnitActionContext? UnitActionContext = null,
    TerrainTile? TerrainTile = null,
    WorldData? WorldData = null,
    int TeamId = 0,
    int? PlayerSkin = null,
    uint SelectionRevision = 0,
    Guid? ArmyId = null,
    Guid? SecondaryArmyId = null,
    Guid? ProductionOrderId = null,
    Guid? SpawnSourceBuildingId = null,
    float ProductionSeconds = 0.0f,
    float ExitX = 0.0f,
    float ExitY = 0.0f,
    float ExitZ = 0.0f,
    Guid? DriverUnitId = null,
    OccupantRole? OccupantRole = null,
    Guid? ProjectileId = null,
    ProjectileKind? ProjectileKind = null,
    float VelocityX = 0.0f,
    float VelocityY = 0.0f,
    float VelocityZ = 0.0f,
    float NormalX = 0.0f,
    float NormalY = 0.0f,
    float NormalZ = 0.0f,
    double ServerTime = 0.0,
    RallyPointState? RallyPoint = null,
    EarthworkOrder? EarthworkOrder = null,
    EarthworkKind? EarthworkKind = null,
    Guid? EarthworkOrderId = null,
    int EarthworkSequence = 0,
    int CellX = 0,
    int CellZ = 0,
    int[]? EarthworkCells = null,
    HelicopterOrder? HelicopterOrder = null,
    TiberiumSeedState? TiberiumSeed = null,
    bool AppendToQueue = false,
    float? FormationFacingDegrees = null,
    UnitRoute[]? Routes = null,
    HarvestPhase? HarvestPhase = null,
    float CargoAmount = 0.0f,
    float TiberiumAmount = 0.0f,
    int ResourceAmount = 0,
    int PurchasePrice = 0,
    int? StartPositionSlot = null,
    MatchStartAssignment[]? MatchStartAssignments = null,
    int ProtocolVersion = 0,
    SessionSnapshot? SessionSnapshot = null,
    bool? SyncDiagnosticsEnabled = null,
    float SyncDiagnosticsIntervalSeconds = 0.0f,
    SyncDiagnosticDigest? SyncDiagnosticDigest = null,
    SyncDiagnosticReport? SyncDiagnosticReport = null,
    ExploredVisibilitySnapshot[]? ExploredVisibility = null,
    SatelliteReconState? SatelliteRecon = null)
{
    public BotControllerOffer? BotOffer { get; init; }
    public AIControllerAssignment? AIControllerAssignment { get; init; }
    public long AIUpdateSequence { get; init; }
    public Guid? AIControllerArmyId { get; init; }
    public Guid? AIControllerActorId { get; init; }
    public long AIControllerGeneration { get; init; }
    public Guid? ControllerPeerId { get; init; }
    public Guid? RequestId { get; set; }
    public long RequestGeneration { get; set; }
    public RequestFeedback? RequestFeedback { get; init; }
}

public sealed record RequestFeedback(Guid RequestId, Guid ActorId, long Generation,
    LocalRequestState State, AIOrderStatus Status, AIOrderFailure Failure, string? Reason,
    Guid? ProductionOrderId, Guid? ConstructionSiteId);
