using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RTS.Mapping;

namespace RTS;

public static class ObjMeshLoader
{
    /// <summary>Sub-mesh of an OBJ model that acts as the rotatable turret.</summary>
    private const string TurretObjectName = "obj_1";

    public static Mesh Load(string path, Color? color = null, UVMapping? cubeMapping = null)
    {        
        List<Vector3> positions = [];
        Dictionary<string, List<VertexPositionColorNormalTexture>> vertices = [];
        Dictionary<string, List<int>> indices = [];
        string objectName = "default";
        Color vertexColor = color ?? Color.SteelBlue;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("o ", StringComparison.Ordinal))
            {
                objectName = line[2..].Trim();
                continue;
            }
            if (line.StartsWith("v ", StringComparison.Ordinal))
            {
                string[] values = line[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 3)
                    positions.Add(new Vector3(
                        float.Parse(values[0], CultureInfo.InvariantCulture),
                        float.Parse(values[2], CultureInfo.InvariantCulture),
                        float.Parse(values[1], CultureInfo.InvariantCulture)));
                continue;
            }
            if (!line.StartsWith("f ", StringComparison.Ordinal))
                continue;

            List<VertexPositionColorNormalTexture> objectVertices = GetOrCreate(vertices, objectName);
            List<int> objectIndices = GetOrCreate(indices, objectName);
            int[] face = line[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => ParsePositionIndex(item, positions.Count)).ToArray();
            for (int index = 1; index < face.Length - 1; index++)
            {
                Vector3 a = positions[face[0]], b = positions[face[index]], c = positions[face[index + 1]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                normal = normal.LengthSquared() > 0.0f ? Vector3.Normalize(normal) : Vector3.Up;
                int first = objectVertices.Count;
                objectVertices.Add(new(a, vertexColor, normal));
                objectVertices.Add(new(b, vertexColor, normal));
                objectVertices.Add(new(c, vertexColor, normal));
                objectIndices.Add(first); objectIndices.Add(first + 1); objectIndices.Add(first + 2);
            }
        }

        List<VertexPositionColorNormalTexture> allVertices = vertices.Values.SelectMany(list => list).ToList();
        if (allVertices.Count == 0)
            throw new InvalidDataException($"OBJ mesh contains no faces: {path}");

        //  normalize: scale this model to 1x1x1
        Vector3 min = new Vector3(0);
        Vector3 max = new Vector3(0);

        for (int index = 0; index < allVertices.Count; index++)
        {
            if (index == 0) {
                min = new(allVertices[index].Position.X, allVertices[index].Position.Y, allVertices[index].Position.Z);
                max = new(allVertices[index].Position.X, allVertices[index].Position.Y, allVertices[index].Position.Z);
                continue;
            }

            min = Vector3.Min(min, allVertices[index].Position);
            max = Vector3.Max(max, allVertices[index].Position);
        }

        Vector3 size = max - min;
        const float ModelScale = 0.1f;

        for (int index = 0; index < allVertices.Count; index++)
        {
            VertexPositionColorNormalTexture vertex = allVertices[index];

            // Nur für Cube-Texture-Mapping normalisieren
            if (cubeMapping is not null)
            {
                Vector3 normalizedPosition = cubeMapping.Normalize(vertex.Position, min, max);

                vertex.TextureCoordinate =
                    GetCubeTextureCoordinate(
                        normalizedPosition,
                        vertex.Normal,
                        cubeMapping);
            }

            // Tatsächliche Geometrie nur uniform skalieren
            vertex.Position *= ModelScale;

            allVertices[index] = vertex;
        }

        int offset = 0;
        foreach (List<VertexPositionColorNormalTexture> objectVertices in vertices.Values)
        {
            for (int index = 0; index < objectVertices.Count; index++)
                objectVertices[index] = allVertices[offset + index];
            offset += objectVertices.Count;
        }

        Mesh mesh = new(Path.GetFileNameWithoutExtension(path), vertices.Select(pair =>
        {
            VertexPositionColorNormalTexture[] objectVertices = pair.Value.ToArray();
            Vector3 pivot = objectVertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex.Position) / objectVertices.Length;
            return new SubMesh(pair.Key, objectVertices, indices[pair.Key].ToArray(), pivot);
        }).ToArray());

        MeshNode? turret = mesh.FindNode(TurretObjectName);
        if (turret is not null)
            turret.RotationParameter = Mesh.TurretAngle;
        return mesh;
    }

    /// <summary>Projects a normalized (0..1) model position onto the atlas tile of the cube face its normal points to.</summary>
    private static Vector2 GetCubeTextureCoordinate(Vector3 position, Vector3 normal, UVMapping mapping)
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
            face = new(isRight ? 1.0f - position.Z : position.Z, 1.0f - position.Y);
        }
        else if (absoluteY >= absoluteZ)
        {
            bool isTop = normal.Y > 0.0f;
            tileColumn = 1;
            tileRow = isTop ? 0 : 1;
            face = new(position.X, isTop ? position.Z : 1.0f - position.Z);
        }
        else
        {
            bool isBack = normal.Z > 0.0f;
            tileColumn = isBack ? 2 : 0;
            tileRow = 1;
            face = new(isBack ? 1.0f - position.X : position.X, 1.0f - position.Y);
        }

        face = mapping.RepeatFace(face);
        float pixelX = mapping.X + (tileColumn + face.X) * mapping.TileSize;
        float pixelY = mapping.Y + (tileRow + face.Y) * mapping.TileSize;
        return new(pixelX / mapping.TextureWidth, pixelY / mapping.TextureHeight);
    }

    private static List<T> GetOrCreate<T>(Dictionary<string, List<T>> values, string key)
    {
        if (!values.TryGetValue(key, out List<T>? result))
        {
            result = [];
            values.Add(key, result);
        }
        return result;
    }

    private static int ParsePositionIndex(string value, int count)
    {
        int index = int.Parse(value.Split('/')[0], CultureInfo.InvariantCulture);
        index = index < 0 ? count + index : index - 1;
        if (index < 0 || index >= count)
            throw new InvalidDataException($"Invalid OBJ vertex index: {value}");
        return index;
    }
}
