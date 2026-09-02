namespace RTS;

public enum UnitActionType
{
    Goto
}

public sealed record UnitAction(
    UnitActionType Type,
    string Name,
    int IconColumn,
    int IconRow);