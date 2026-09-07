using Microsoft.Xna.Framework;
using System;

namespace RTS;

public readonly record struct HitInfo(Guid? SourceUnitId, Vector3 Position, float Damage);
