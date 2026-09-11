using Microsoft.Xna.Framework;
using System;

namespace RTS.Mapping;

public sealed record CubeTileMapping(
    int TileSize,
    int X,
    int Y,
    int TextureWidth,
    int TextureHeight,
    float TileRepeat = 1.0f)
    : UVMapping(TileSize, X, Y, TextureWidth, TextureHeight, TileRepeat)
{
    public override Vector3 Normalize(Vector3 position, Vector3 minimum, Vector3 maximum)
    {
        Vector3 size = maximum - minimum;
        float maximumSize = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (maximumSize <= 0.0f)
            return new Vector3(0.5f);

        Vector3 normalizedPosition = (position - minimum) / size;
        return normalizedPosition;
    }
}