using Microsoft.Xna.Framework;
using System;

namespace RTS.Mapping;

/// <summary>
/// Base class for UV projections into the unit texture atlas. A mapping owns
/// both the atlas position and the rule used to normalize mesh coordinates.
/// </summary>
public abstract record UVMapping(
    int TileSize,
    int X,
    int Y,
    int TextureWidth,
    int TextureHeight,
    float TileRepeat = 1.0f)
{
    /// <summary>Normalizes a point to the [0..1] cube space used by cube projection.</summary>
    public virtual Vector3 Normalize(Vector3 position, Vector3 minimum, Vector3 maximum)
    {
        Vector3 size = maximum - minimum;
        return new(
            size.X == 0.0f ? 0.5f : (position.X - minimum.X) / size.X,
            size.Y == 0.0f ? 0.5f : (position.Y - minimum.Y) / size.Y,
            size.Z == 0.0f ? 0.5f : (position.Z - minimum.Z) / size.Z);
    }

    /// <summary>
    /// Repeats a UV inside its individual atlas tile. The repeat is resolved
    /// before atlas coordinates are generated, so it cannot spill into a
    /// neighboring tile. A value of 2 renders two repeats per cube face.
    /// </summary>
    public Vector2 RepeatFace(Vector2 face) => new(
        RepeatCoordinate(face.X),
        RepeatCoordinate(face.Y));

    private float RepeatCoordinate(float value)
    {
        // Preserve the original mapping exactly for one tile. A face edge at
        // 1.0 must remain 1.0; wrapping it to 0.0 stretches a triangle across
        // the tile and creates the visible stripe on the cube sides.
        if (MathF.Abs(TileRepeat - 1.0f) <= 0.0001f)
            return value;

        float repeated = value * MathF.Max(TileRepeat, 0.0001f);
        return repeated - MathF.Floor(repeated);
    }
}
