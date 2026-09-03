namespace RTS;

public enum UnitActionType
{
    Goto,
    RaiseTerrain,
    LowerTerrain,
    FlattenTerrain,
    SmoothTerrain,
    IncToolSize,
    DecToolSize,
    SetTerrainTile,
    GetTerrainTile,
    NextTerrainTile,
    PrevTerrainTile,
    SelectToolCircle,
    SelectToolRectangle,
    SelectToolFill

}

public sealed record UnitAction(
    UnitActionType Type,
    string Name,
    int IconColumn,
    int IconRow);