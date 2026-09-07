using System;

namespace RTS;

public sealed record UnitState(
    Guid UnitId,
    uint Revision,
    string TypeId,
    int Version,
    byte[] Payload);
