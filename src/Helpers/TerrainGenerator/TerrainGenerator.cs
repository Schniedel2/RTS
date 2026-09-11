using System;
using Microsoft.Xna.Framework;

namespace RTS;

/// <summary>
/// Local, editor-oriented terrain generation helpers. Each public method
/// rebuilds the terrain mesh once after applying its complete operation.
/// </summary>
public static class TerrainGenerator
{
    public static void AddHills(Terrain terrain, int count, float radius, float height, int seed)
    {
        if (count <= 0 || radius <= 0.0f || height == 0.0f)
            return;

        Random random = new(seed);
        float margin = MathF.Min(radius, MathF.Min(terrain.Width, terrain.Height) * 0.25f);
        for (int index = 0; index < count; index++)
        {
            float x = RandomRange(random, margin, terrain.Width - 1 - margin);
            float z = RandomRange(random, margin, terrain.Height - 1 - margin);
            AddHillCore(terrain, x, z, radius * RandomRange(random, 0.7f, 1.3f),
                height * RandomRange(random, 0.65f, 1.15f));
        }

        terrain.BuildTerrainMesh();
    }

    public static void AddMountain(Terrain terrain, float x, float z, float radius, float height)
    {
        if (radius <= 0.0f || height == 0.0f)
            return;

        AddHillCore(terrain, x, z, radius, height);
        AddHillCore(terrain, x - radius * 0.18f, z + radius * 0.12f, radius * 0.55f, height * 0.55f);
        AddHillCore(terrain, x + radius * 0.22f, z - radius * 0.16f, radius * 0.45f, height * 0.45f);
        terrain.BuildTerrainMesh();
    }

    public static void AddRiver(
        Terrain terrain,
        Vector2 start,
        Vector2 end,
        float width,
        float depth)
    {
        if (width <= 0.0f || depth <= 0.0f)
            return;

        Vector2 line = end - start;
        float lineLengthSquared = line.LengthSquared();
        if (lineLengthSquared <= 0.0001f)
            return;

        for (int z = 0; z < terrain.Height; z++)
        for (int x = 0; x < terrain.Width; x++)
        {
            Vector2 point = new(x, z);
            float projection = MathHelper.Clamp(Vector2.Dot(point - start, line) / lineLengthSquared, 0.0f, 1.0f);
            float distance = Vector2.Distance(point, start + line * projection);
            if (distance > width)
                continue;

            float falloff = 1.0f - distance / width;
            terrain.SetHeight(x, z, terrain.GetHeight(x, z) - depth * falloff * falloff);
            if (distance <= width * 0.75f)
                terrain.SetTile(x, z, TerrainTile.Sand);
        }

        terrain.UpdateTilemapTexture();
        terrain.BuildTerrainMesh();
    }

    public static void Smooth(Terrain terrain, float blend)
    {
        blend = MathHelper.Clamp(blend, 0.0f, 1.0f);
        if (blend <= 0.0f)
            return;

        float[,] heights = new float[terrain.Width, terrain.Height];
        for (int z = 0; z < terrain.Height; z++)
        for (int x = 0; x < terrain.Width; x++)
        {
            float sum = 0.0f;
            int samples = 0;
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                int sampleX = x + offsetX;
                int sampleZ = z + offsetZ;
                if (!TerrainHelper.IsInsideMap(terrain, sampleX, sampleZ))
                    continue;
                sum += terrain.GetHeight(sampleX, sampleZ);
                samples++;
            }
            heights[x, z] = MathHelper.Lerp(terrain.GetHeight(x, z), sum / samples, blend);
        }

        ApplyHeights(terrain, heights);
    }

    /// <summary>Blends each cell towards its mirrored counterpart instead of copying it exactly.</summary>
    public static void ImproveSymmetry(Terrain terrain, bool horizontal, float blend)
    {
        blend = MathHelper.Clamp(blend, 0.0f, 1.0f);
        if (blend <= 0.0f)
            return;

        float[,] heights = new float[terrain.Width, terrain.Height];
        for (int z = 0; z < terrain.Height; z++)
        for (int x = 0; x < terrain.Width; x++)
        {
            int mirrorX = horizontal ? terrain.Width - 1 - x : x;
            int mirrorZ = horizontal ? z : terrain.Height - 1 - z;
            float average = (terrain.GetHeight(x, z) + terrain.GetHeight(mirrorX, mirrorZ)) * 0.5f;
            heights[x, z] = MathHelper.Lerp(terrain.GetHeight(x, z), average, blend);
        }

        ApplyHeights(terrain, heights);
    }

    private static void AddHillCore(Terrain terrain, float centerX, float centerZ, float radius, float height)
    {
        int minimumX = Math.Max(0, (int)MathF.Floor(centerX - radius));
        int maximumX = Math.Min(terrain.Width - 1, (int)MathF.Ceiling(centerX + radius));
        int minimumZ = Math.Max(0, (int)MathF.Floor(centerZ - radius));
        int maximumZ = Math.Min(terrain.Height - 1, (int)MathF.Ceiling(centerZ + radius));
        for (int z = minimumZ; z <= maximumZ; z++)
        for (int x = minimumX; x <= maximumX; x++)
        {
            float distance = Vector2.Distance(new Vector2(x, z), new Vector2(centerX, centerZ));
            if (distance > radius)
                continue;
            float falloff = 1.0f - distance / radius;
            terrain.SetHeight(x, z, terrain.GetHeight(x, z) + height * falloff * falloff);
        }
    }

    private static void ApplyHeights(Terrain terrain, float[,] heights)
    {
        for (int z = 0; z < terrain.Height; z++)
        for (int x = 0; x < terrain.Width; x++)
            terrain.SetHeight(x, z, heights[x, z]);
        terrain.BuildTerrainMesh();
    }

    private static float RandomRange(Random random, float minimum, float maximum) =>
        minimum + (float)random.NextDouble() * (maximum - minimum);
}
