using Microsoft.Xna.Framework;

namespace RTS;

public readonly record struct GotoCommand(Vector2 Target);