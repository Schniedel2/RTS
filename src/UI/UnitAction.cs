namespace RTS;

public enum UnitActionType
{
    Filler,
    Goto,
    Attack,
    Build,
    BuildConstruction,
    RaiseTerrain,
    LowerTerrain,
    FlattenTerrain,
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
    max

}

public sealed record UnitAction(
    UnitActionType Type,
    string Name,
    int IconColumn,
    int IconRow,
    string TargetObjectName = "");
