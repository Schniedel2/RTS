using System;

namespace RTS;

public sealed record MapObjectState(
    Guid Id,
    string TypeId,
    float X,
    float Y,
    float Z,
    float RotationDegrees = 0);
