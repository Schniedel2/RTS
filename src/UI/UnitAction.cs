namespace RTS;

public enum UnitActionType
{    
    None,
    Goto,
    Stop,
    Attack,
    Build,
    BuildConstruction,
    Follow,
    TrainUnit,
    EnterUnit,
    LeaveContainer,
    Repair,
    RaiseTerrain,
    LowerTerrain,
    FlattenTerrain,
    SharpenTerrain,
    SmoothTerrain,
    IncToolSize,
    DecToolSize,
    SetTerrainTile,
    GetTerrainTile,
    FillTile,
    SelectToolCircle,
    SelectToolRectangle,
    SelectToolDither,
    Save,
    //  action with alternate behavior
    TilePreview,
    AdjustToolSize,
    AdjustTerrainSize,
    SetRallyPoint,
    ClearRallyPoint,
    LevelAndConcrete,
    RemoveConcrete,
    PlaceGameplayMarker,
    DeleteGameplayMarker,
    TakeOff,
    Land,
    ReturnToHelipad,
    max

}

public sealed record UnitAction(
    UnitActionType Type,
    string Name,
    int IconColumn,
    int IconRow,
    string TargetObjectName = "",
    GameplayMarkerType? MarkerType = null);
