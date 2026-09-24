using Microsoft.Xna.Framework;
using System;

namespace RTS;

public sealed record UnitActionPosition(float X, float Y, float Z)
{
    public Vector3 ToVector3() => new(X, Y, Z);
    public static UnitActionPosition From(Vector3 position) => new(position.X, position.Y, position.Z);
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}

/// <summary>Small, typed parameter bag shared by host-confirmed unit actions.</summary>
public sealed record UnitActionContext(
    UnitActionPosition? TargetPosition = null,
    Guid? TargetUnitId = null,
    string? Value = null,
    int? IntValue = null,
    float? FloatValue = null,
    bool Alternate = false)
{
    public static UnitActionContext Empty { get; } = new();
    public static UnitActionContext At(Vector3 position, Guid? targetUnitId = null, bool alternate = false) =>
        new(UnitActionPosition.From(position), targetUnitId, Alternate: alternate);
}
