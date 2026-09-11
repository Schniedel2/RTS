using Microsoft.Xna.Framework;
using System;

namespace RTS;

/// <summary>Applies box projection UVs to a complete mesh or an individual sub-mesh.</summary>
public static class CubeMapping
{
    /// <summary>
    /// Maps every part against the complete mesh bounds. This keeps UV scale
    /// consistent across separately named objects such as tank hull and turret.
    /// </summary>
    public static void Apply(Mesh mesh, CubeTileMapping mapping)
    {
        (Vector3 min, Vector3 max) = mesh.GetBounds();
        foreach (SubMesh subMesh in mesh.SubMeshes)
            Apply(subMesh, mapping, min, max);
    }

    static bool NameMacthes(string name, string pattern)
    {
        if (pattern.EndsWith("*"))
        {
            string prefix = pattern.TrimEnd('*');
            return name.StartsWith(prefix);
        }
        return name == pattern;
    }

    public static void Apply(string meshNamePath, CubeTileMapping mapping)
    {
        string meshName = meshNamePath.Split("/")[0]; // Get the file name from the path
        string submeshPath = meshNamePath.Substring(meshName.Length + 1);

        foreach (string name in Globals.MeshHandler.Meshes.Keys)
        {
            if (NameMacthes(name, meshName))
            foreach (SubMesh subMesh in Globals.MeshHandler.Meshes[name].SubMeshes)            
                {
                    if (NameMacthes(subMesh.Name, submeshPath))                    
                        Apply(subMesh, mapping);
                }
        }
    }

    /// <summary>Maps one part against its own bounds.</summary>
    public static void Apply(SubMesh subMesh, CubeTileMapping mapping)
    {
        (Vector3 min, Vector3 max) = subMesh.GetBounds();
        Apply(subMesh, mapping, min, max);
    }

    /// <summary>Projects one normalized (0..1) point to the appropriate atlas face.</summary>
    public static Vector2 GetTextureCoordinate(
        Vector3 normalizedPosition,
        Vector3 normal,
        CubeTileMapping mapping)
    {
        float absoluteX = Math.Abs(normal.X);
        float absoluteY = Math.Abs(normal.Y);
        float absoluteZ = Math.Abs(normal.Z);

        int tileColumn;
        int tileRow;
        Vector2 face;

        if (absoluteX >= absoluteY && absoluteX >= absoluteZ)
        {
            bool isRight = normal.X > 0.0f;
            tileColumn = isRight ? 2 : 0;
            tileRow = 0;
            face = new(isRight ? 1.0f - normalizedPosition.Z : normalizedPosition.Z,
                1.0f - normalizedPosition.Y);
        }
        else if (absoluteY >= absoluteZ)
        {
            bool isTop = normal.Y > 0.0f;
            tileColumn = 1;
            tileRow = isTop ? 0 : 1;
            face = new(normalizedPosition.X,
                isTop ? normalizedPosition.Z : 1.0f - normalizedPosition.Z);
        }
        else
        {
            bool isBack = normal.Z > 0.0f;
            tileColumn = isBack ? 2 : 0;
            tileRow = 1;
            face = new(isBack ? 1.0f - normalizedPosition.X : normalizedPosition.X,
                1.0f - normalizedPosition.Y);
        }

        float pixelX = mapping.X + (tileColumn + face.X) * mapping.TileSize;
        float pixelY = mapping.Y + (tileRow + face.Y) * mapping.TileSize;
        return new(pixelX / mapping.TextureWidth, pixelY / mapping.TextureHeight);
    }

    private static void Apply(
        SubMesh subMesh,
        CubeTileMapping mapping,
        Vector3 minimum,
        Vector3 maximum)
    {
        Vector3 size = maximum - minimum;
        for (int index = 0; index < subMesh.Vertices.Length; index++)
        {
            VertexPositionColorNormalTexture vertex = subMesh.Vertices[index];
            Vector3 normalizedPosition = Normalize(vertex.Position, minimum, size);
            vertex.TextureCoordinate = GetTextureCoordinate(
                normalizedPosition,
                vertex.Normal,
                mapping);
            subMesh.Vertices[index] = vertex;
        }
    }

    private static Vector3 Normalize(Vector3 position, Vector3 minimum, Vector3 size) =>
        new(
            size.X == 0.0f ? 0.5f : (position.X - minimum.X) / size.X,
            size.Y == 0.0f ? 0.5f : (position.Y - minimum.Y) / size.Y,
            size.Z == 0.0f ? 0.5f : (position.Z - minimum.Z) / size.Z);
}
